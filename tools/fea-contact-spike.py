"""Bounded CPU contact verification. Synthetic inputs; no calibrated material or part claim.

Run with Python + psutil, using the already pinned external CalculiX 2.23 tool.
All decks, solver output and numerical evidence are retained under artifacts.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import subprocess
import time

import psutil

ROOT = Path(__file__).resolve().parents[1]
CCX = ROOT / "artifacts/fea-toolchain/calculix-2.23/calculix_2.23_4win/ccx_static.exe"
CCX_SHA = "e18360eb32a5cd48f91a212c57069212cd8e27d2f798a599a78106bd0b3ecc1f"
OUT = ROOT / "artifacts/fea-contact-spike"
GMSH = ROOT / "artifacts/fea-toolchain/gmsh-4.15.2/gmsh-4.15.2-Windows64/gmsh.exe"
GMSH_SHA = "317c43391e5b1fab3a1dd80dc5245dad6e2d087910f4b8ebc234bd6d4b8f41a1"


def invoke(folder: Path, deck: str, timeout: float = 120) -> dict:
    if hashlib.sha256(CCX.read_bytes()).hexdigest() != CCX_SHA:
        raise ValueError("CalculiX binary differs from the reviewed pin")
    folder.mkdir(parents=True, exist_ok=True)
    (folder / "case.inp").write_text(deck, encoding="ascii")
    env = dict(os.environ, OMP_NUM_THREADS="4", CCX_NPROC_RESULTS="4", CCX_NPROC_EQUATION_SOLVER="4")
    start = time.perf_counter()
    peak = 0
    with (folder / "stdout.log").open("w") as stdout, (folder / "stderr.log").open("w") as stderr:
        process = subprocess.Popen([str(CCX), "-i", "case"], cwd=folder, env=env, stdout=stdout, stderr=stderr,
                                   creationflags=subprocess.CREATE_NO_WINDOW)
        monitor = psutil.Process(process.pid)
        try:
            while process.poll() is None:
                try:
                    peak = max(peak, monitor.memory_info().rss)
                except psutil.NoSuchProcess:
                    pass
                if time.perf_counter() - start > timeout or peak > 2 * 1024**3:
                    raise TimeoutError("Contact job exceeded its time/2-GB memory budget")
                time.sleep(.05)
        finally:
            if process.poll() is None:
                for child in monitor.children(recursive=True):
                    child.kill()
                process.kill()
                process.wait()
    log = (folder / "stdout.log").read_text(errors="replace")
    if process.returncode or "*ERROR" in log or "Job finished" not in log:
        raise RuntimeError(f"Contact solver failed; inspect {folder / 'stdout.log'}")
    return {"seconds": time.perf_counter() - start, "peakWorkingSetBytes": peak,
            "deckSha256": hashlib.sha256(deck.encode("ascii")).hexdigest(), "solverSha256": CCX_SHA}


def totals(path: Path, name: str) -> list[tuple[float, list[float]]]:
    pattern = rf"total force \(fx,fy,fz\) for set {name} and time\s+([-+\d.Ee]+)\s+([-+\d.Ee]+)\s+([-+\d.Ee]+)\s+([-+\d.Ee]+)"
    return [(float(m[0]), list(map(float, m[1:]))) for m in re.findall(pattern, path.read_text())]


def ids(values: list[int]) -> str:
    return "\n".join(",".join(map(str,values[start:start+16])) for start in range(0,len(values),16))


def step_mesh(size: str) -> tuple[dict[int, tuple[float,float,float]], dict[int,list[int]], str]:
    if hashlib.sha256(GMSH.read_bytes()).hexdigest()!=GMSH_SHA:
        raise ValueError("Mesher differs from its reviewed pin")
    folder=ROOT/"artifacts/fea-step-mesh-spike"
    mesh=folder/f"mesh-{size}.msh"
    target=folder/f"contact-{size}.inp"
    conversion=subprocess.run([str(GMSH),str(mesh),"-0","-format","inp","-o",str(target),"-nt","4"],cwd=folder,
                              capture_output=True,text=True,timeout=30,creationflags=subprocess.CREATE_NO_WINDOW)
    (folder/f"contact-{size}.conversion.log").write_text(conversion.stdout+conversion.stderr)
    if conversion.returncode:
        raise ValueError("Gmsh's native Abaqus conversion failed")
    nodes, elements={},{}
    mode=None
    for line in target.read_text().splitlines():
        if line.startswith("*"):
            mode="node" if line.upper().startswith("*NODE") else "element" if line.upper().startswith("*ELEMENT") and "C3D10" in line.upper() else None
        elif line.strip():
            fields=[v.strip() for v in line.split(",")]
            if mode=="node":
                nodes[int(fields[0])]=tuple(map(float,fields[1:4]))
            elif mode=="element":
                element=list(map(int,fields[1:]))
                if len(element)!=10:
                    raise ValueError("Expected quadratic tetrahedra")
                elements[int(fields[0])]=element
    if not nodes or not elements or abs(max(p[2] for p in nodes.values())-.04)>1e-8:
        raise ValueError("STEP mesh units or topology are invalid")
    return nodes,elements,hashlib.sha256(mesh.read_bytes()).hexdigest()


TET_FACES=[(0,1,2,4,5,6),(0,3,1,7,8,4),(1,3,2,8,9,5),(2,3,0,9,7,6)]


def quadratic_quality(nodes: dict, elements: dict) -> dict:
    edges=[(0,1),(1,2),(0,2),(0,3),(1,3),(2,3)]
    gradients=[(-1.,-1.,-1.),(1.,0.,0.),(0.,1.,0.),(0.,0.,1.)]
    points=[(.25,)*4]+[tuple(.5854101966249685 if i==k else .1381966011250105 for i in range(4)) for k in range(4)]
    points += [tuple(1. if i==k else 0. for i in range(4)) for k in range(4)]
    points += [tuple(.5 if i in edge else 0. for i in range(4)) for edge in edges]
    minimum=float("inf")
    for element, vertices in elements.items():
        for l in points:
            dn=[tuple((4*l[i]-1)*v for v in gradients[i]) for i in range(4)]
            dn += [tuple(4*(gradients[a][k]*l[b]+l[a]*gradients[b][k]) for k in range(3)) for a,b in edges]
            j=[[sum(nodes[vertices[i]][r]*dn[i][c] for i in range(10)) for c in range(3)] for r in range(3)]
            determinant=j[0][0]*(j[1][1]*j[2][2]-j[1][2]*j[2][1])-j[0][1]*(j[1][0]*j[2][2]-j[1][2]*j[2][0])+j[0][2]*(j[1][0]*j[2][1]-j[1][1]*j[2][0])
            scale=math.prod(math.sqrt(sum(j[r][c]**2 for r in range(3))) for c in range(3))
            if determinant<=0 or not math.isfinite(determinant) or scale<=0:
                raise ValueError(f"Quadratic mesh has a nonpositive sampled Jacobian in element {element}")
            minimum=min(minimum,determinant/scale)
    return {"minimumSampledScaledJacobian":minimum,"locationsPerTet":len(points),"basis":"CCX quadrature plus centroid, corners and edge midpoints; not a proof of global bijectivity"}


def step_deck(nodes: dict, elements: dict, fraction: float=.3, penalty: float=1e12, fold: bool=False, increment: float=.025) -> tuple[str,dict]:
    boundary={}
    for element, vertex in elements.items():
        for face, pattern in enumerate(TET_FACES,1):
            indices=[vertex[i] for i in pattern]
            key=tuple(sorted(indices[:3]))
            boundary.setdefault(key,[]).append((element,face,indices))
    faces=[records[0] for records in boundary.values() if len(records)==1]
    def at_height(z):
        return [(e,f,ns) for e,f,ns in faces if all(abs(nodes[n][2]-z)<1e-8 for n in ns)]
    top,bottom=at_height(.04),at_height(0)
    if not top or not bottom:
        raise ValueError("STEP contact faces could not be identified")
    lower,upper=at_height(.016),at_height(.024)
    if fold and (not lower or not upper):
        raise ValueError("The declared groove faces do not exist")
    all_nodes=dict(nodes)
    plate=[]
    for point in [(-.04,-.04,.04),(.04,-.04,.04),(.04,.04,.04),(-.04,.04,.04),
                  (-.04,-.04,.045),(.04,-.04,.045),(.04,.04,.045),(-.04,.04,.045)]:
        key=max(all_nodes)+1
        all_nodes[key]=point
        plate.append(key)
    plate_id=max(elements)+1
    surface=lambda values:"\n".join(f"{e},S{f}" for e,f,_ in values)
    # CalculiX accepts at most 20 characters per numeric field; 12 digits retain sub-nm SI geometry here.
    node_text="\n".join(str(i)+","+",".join(f"{v:.12g}" for v in p) for i,p in all_nodes.items())
    element_text="\n".join(str(i)+","+",".join(map(str,ns)) for i,ns in elements.items())
    bottom_nodes=sorted({n for _,_,ns in bottom for n in ns})
    top_nodes=sorted({n for _,_,ns in top for n in ns})
    deck=f"""*HEADING
Asymmetric STEP numerical benchmark; synthetic NH material; bonded lower mounting face and frictionless rigid upper platen; SI
*NODE
{node_text}
*ELEMENT,TYPE=C3D10,ELSET=FOAM
{element_text}
*ELEMENT,TYPE=C3D8,ELSET=PLATE
{plate_id},{','.join(map(str,plate))}
*NSET,NSET=BOTTOM
{ids(bottom_nodes)}
*NSET,NSET=TOP
{ids(top_nodes)}
*NSET,NSET=PLATEN
{ids(plate)}
*MATERIAL,NAME=EDUCATIONAL
*HYPERELASTIC,NEO HOOKE
500000.,2.E-8
*SOLID SECTION,ELSET=FOAM,MATERIAL=EDUCATIONAL
*MATERIAL,NAME=FIXTURE
*ELASTIC
2.E11,.3
*SOLID SECTION,ELSET=PLATE,MATERIAL=FIXTURE
*SURFACE,NAME=FOAMTOP
{surface(top)}
*SURFACE,NAME=PLATEBOTTOM
{plate_id},S1
*SURFACE INTERACTION,NAME=CONTACT
*SURFACE BEHAVIOR,PRESSURE-OVERCLOSURE=LINEAR
{penalty:.16g}
*CONTACT PAIR,INTERACTION=CONTACT,TYPE=SURFACE TO SURFACE
FOAMTOP,PLATEBOTTOM
"""
    if fold:
        deck+=f"""*SURFACE,NAME=GROOVEUPPER
{surface(upper)}
*SURFACE,NAME=GROOVELOWER
{surface(lower)}
*CONTACT PAIR,INTERACTION=CONTACT,TYPE=SURFACE TO SURFACE
GROOVEUPPER,GROOVELOWER
"""
    deck+=f"""*BOUNDARY
BOTTOM,1,3,0.
PLATEN,1,2,0.
*STEP,NLGEOM,INC=500
*STATIC,SOLVER=SPOOLES
{increment:.12g},1.,1.E-8,{increment:.12g}
*BOUNDARY
PLATEN,3,3,{-fraction*.04:.16g}
*NODE PRINT,NSET=PLATEN,TOTALS=YES
RF
*NODE PRINT,NSET=BOTTOM,TOTALS=YES
RF
*NODE FILE
U
*CONTACT PRINT
CDIS,CSTR
*END STEP
"""
    return deck,{"nodes":len(nodes),"quadraticTets":len(elements),"topFaces":len(top),"bottomFaces":len(bottom),"grooveUpperFaces":[[e,f] for e,f,_ in upper]}


def contact_values(path: Path, title: str) -> list[tuple[float,int,int,float]]:
    lines=path.read_text().splitlines()
    result=[]
    for i,line in enumerate(lines):
        if not line.strip().startswith(title) or "and time" not in line:
            continue
        t=float(line.split("and time")[-1])
        j=i+1
        while j<len(lines) and not lines[j].strip():
            j+=1
        while j<len(lines) and lines[j].strip():
            fields=lines[j].split()
            if len(fields)==5:
                result.append((t,int(fields[0]),int(fields[1]),float(fields[2])))
            j+=1
    return result


def step_compression(mesh_size: str, fraction: float, fold: bool, penalty: float=1e12, increment: float=.025) -> dict:
    if not math.isfinite(fraction) or not 0<fraction<1:
        raise ValueError("Compression must be a finite fraction strictly between zero and one")
    nodes,elements,mesh_hash=step_mesh(mesh_size)
    quality=quadratic_quality(nodes,elements)
    deck,metadata=step_deck(nodes,elements,fraction=fraction,fold=fold,penalty=penalty,increment=increment)
    fingerprint=hashlib.sha256(deck.encode("ascii")).hexdigest()
    folder=OUT/"step"/f"{mesh_size}-{fraction:.2f}{'-fold' if fold else ''}-{fingerprint[:12]}"
    cached=folder/"verification.json"
    if cached.exists():
        report=json.loads(cached.read_text())
        if report["deckSha256"]==fingerprint and report["meshSha256"]==mesh_hash and report["solverSha256"]==CCX_SHA:
            return {"status":"Reused exact numerically accepted STEP compression input", "results":[report]}
    report=invoke(folder,deck,timeout=240)
    top=totals(folder/"case.dat","PLATEN")
    bottom=totals(folder/"case.dat","BOTTOM")
    if not top or abs(top[-1][0]-1)>1e-8 or not bottom:
        raise ValueError("STEP solve returned incomplete history")
    force=-top[-1][1][2]
    scale=max(abs(f[2]) for _,f in top)
    imbalance=max(abs(a[1][2]+b[1][2]) for a,b in zip(top,bottom))
    displacements=contact_values(folder/"case.dat","relative contact displacement")
    penetration=max((abs(value) for _,_,_,value in displacements),default=float("inf"))
    if force<=0 or imbalance>max(.1,.01*scale) or penetration>1e-5:
        raise ValueError(f"STEP force/reaction/10-um contact-gap gate failed: F={force}, imbalance={imbalance}, gap={penetration}")
    groove=set(map(tuple,metadata["grooveUpperFaces"]))
    closing=[(t,pressure) for t,e,f,pressure in contact_values(folder/"case.dat","contact stress") if (e,f) in groove and pressure>1000]
    report.update(metadata,meshSha256=mesh_hash,compressionFraction=fraction,forceN=force,imbalanceN=imbalance,
                  samples=[{"compressionM":.04*fraction*t,"forceN":-f[2]} for t,f in top],
                  fixture="Bonded lower face; frictionless rigid upper contact; no shaft in this case",foldContactEnabled=fold,initialMeshQuality=quality,
                  maximumAbsoluteContactNormalDisplacementM=penetration,declaredFixtureToleranceM=1e-4,grooveContactDetected=bool(closing),
                  firstGrooveContactFraction=closing[0][0] if closing else None)
    (folder/"verification.json").write_text(json.dumps(report,indent=2))
    return {"status":"Asymmetric STEP contact compression; mesh convergence and folding require their separate gates", "results":[report]}


def interpolate(samples: list[dict], compression: float) -> float:
    table=[{"compressionM":0.,"forceN":0.}]+samples
    for a,b in zip(table,table[1:]):
        if a["compressionM"]<=compression<=b["compressionM"]+1e-12:
            t=(compression-a["compressionM"])/(b["compressionM"]-a["compressionM"])
            return a["forceN"]+t*(b["forceN"]-a["forceN"])
    raise ValueError("No extrapolation outside solved samples")


def mesh_convergence(fraction: float) -> dict:
    results=[step_compression(size,fraction,False)["results"][0] for size in ("0_0060","0_0045","0_0035")]
    medium,fine=results[1:]
    force_scale=max(point["forceN"] for point in fine["samples"])
    error=max(abs(interpolate(medium["samples"],.04*fraction*t/40)-interpolate(fine["samples"],.04*fraction*t/40)) for t in range(41))
    if error>max(.1,.05*force_scale):
        raise ValueError(f"Medium/fine curve difference {error} exceeds 5% of declared force scale {force_scale}")
    return {"status":"Three-mesh asymmetric STEP contact force convergence; no calibrated material or fold/locking claim", "mediumFineMaximumDifferenceN":error,"forceScaleN":force_scale,"relativeDifference":error/force_scale,"results":results}


def cube_contact(penalty: float, increment: float, compression: float=.3, material: str="*HYPERELASTIC,NEO HOOKE\n500000.,2.E-8") -> str:
    nodes = [(0, 0, 0), (.02, 0, 0), (.02, .02, 0), (0, .02, 0),
             (0, 0, .02), (.02, 0, .02), (.02, .02, .02), (0, .02, .02),
             (-.01, -.01, .02), (.04, -.01, .02), (.04, .04, .02), (-.01, .04, .02),
             (-.01, -.01, .024), (.04, -.01, .024), (.04, .04, .024), (-.01, .04, .024)]
    node_text = "\n".join(f"{i+1}," + ",".join(f"{v:.16g}" for v in p) for i, p in enumerate(nodes))
    return f"""*HEADING
Synthetic finite-strain coupon with a prescribed rigid platen and frictionless face contact; SI
*NODE
{node_text}
*ELEMENT,TYPE=C3D8,ELSET=FOAM
1,1,2,3,4,5,6,7,8
*ELEMENT,TYPE=C3D8,ELSET=PLATE
2,9,10,11,12,13,14,15,16
*NSET,NSET=BOTTOM
1,2,3,4
*NSET,NSET=TOP
5,6,7,8
*NSET,NSET=PLATEN
9,10,11,12,13,14,15,16
*MATERIAL,NAME=EDUCATIONAL
{material}
*SOLID SECTION,ELSET=FOAM,MATERIAL=EDUCATIONAL
*MATERIAL,NAME=FIXTURE
*ELASTIC
2.E11,.3
*SOLID SECTION,ELSET=PLATE,MATERIAL=FIXTURE
*SURFACE,NAME=FOAMTOP
1,S2
*SURFACE,NAME=PLATEBOTTOM
2,S1
*SURFACE INTERACTION,NAME=CONTACT
*SURFACE BEHAVIOR,PRESSURE-OVERCLOSURE=LINEAR
{penalty:.16g}
*CONTACT PAIR,INTERACTION=CONTACT,TYPE=SURFACE TO SURFACE
FOAMTOP,PLATEBOTTOM
*BOUNDARY
BOTTOM,3,3,0.
1,1,2,0.
2,2,2,0.
PLATEN,1,2,0.
*STEP,NLGEOM,INC=300
*STATIC,SOLVER=SPOOLES
{increment:.16g},1.,1.E-8,{increment:.16g}
*BOUNDARY
PLATEN,3,3,{-compression*.02:.12g}
*NODE PRINT,NSET=PLATEN,TOTALS=YES
RF
*NODE PRINT,NSET=BOTTOM,TOTALS=YES
RF
*NODE PRINT,NSET=TOP
U
*NODE FILE
U
*CONTACT PRINT
CDIS,CSTR
*END STEP
"""


def neo_hooke_nominal(axial: float, c10: float = 500_000, d1: float = 2e-8) -> float:
    # Independent traction-free lateral stretch from differentiating compressible NH energy.
    def nominal(lateral: float, direction: int) -> float:
        stretches = [lateral, lateral, axial]
        j = lateral * lateral * axial
        i1 = sum(v * v for v in stretches)
        value = stretches[direction]
        return 2*c10*j**(-2/3)*(value-i1/(3*value)) + 2/d1*(j-1)*j/value
    lo, hi = .5, 2.
    for _ in range(100):
        mid = (lo+hi)/2
        if nominal(mid, 0) > 0:
            hi = mid
        else:
            lo = mid
    return -nominal((lo+hi)/2, 2)


def coupon() -> dict:
    results = []
    expected = neo_hooke_nominal(.7)*.02**2
    for name, penalty, increment in [("nominal", 1e12, .05), ("stiffer-contact", 4e12, .05), ("smaller-increment", 1e12, .025)]:
        folder = OUT / "coupon" / name
        report = invoke(folder, cube_contact(penalty, increment))
        top = totals(folder / "case.dat", "PLATEN")
        bottom = totals(folder / "case.dat", "BOTTOM")
        if not top or abs(top[-1][0]-1) > 1e-8 or not bottom:
            raise ValueError("No complete reaction history; partial solve rejected")
        force = -top[-1][1][2]
        imbalance = abs(top[-1][1][2]+bottom[-1][1][2])
        displacement = re.findall(r"displacements \(vx,vy,vz\) for set TOP and time\s+([-+\d.Ee]+)\s+((?:\s*\d+\s+[-+\d.Ee]+\s+[-+\d.Ee]+\s+[-+\d.Ee]+\s*)+)", (folder / "case.dat").read_text())
        penetration = None
        if displacement:
            rows = [line.split() for line in displacement[-1][1].strip().splitlines() if line.strip()]
            penetration = max(float(row[3])+.006 for row in rows)
        report.update(name=name, forceN=force, expectedForceN=expected, relativeAnalyticError=abs(force-expected)/expected,
                      imbalanceN=imbalance, penetrationM=penetration, penaltyPaPerM=penalty, increment=increment,
                      samples=[{"fraction": t, "compressionM": .006*t, "forceN": -f[2]} for t, f in top])
        if report["relativeAnalyticError"] > .01 or imbalance/expected > .01 or penetration is None or not 0 <= penetration < 2e-6:
            raise ValueError("Contact coupon failed analytic/balance/penetration gate: " + json.dumps(report))
        (folder / "verification.json").write_text(json.dumps(report, indent=2))
        results.append(report)
    return {"status": "Numerical contact coupon only; does not establish part contact or locking", "results": results}


def axis_deck(nr: int, nz: int, contact: bool) -> tuple[str, str]:
    nodes: dict[int, tuple[float, float, float]] = {}
    lookup: dict[tuple[int, int], int] = {}
    for j in range(2*nz+1):
        for i in range(2*nr+1):
            if i % 2 and j % 2:
                continue
            key = len(nodes)+1
            lookup[i, j] = key
            nodes[key] = (.004+.016*i/(2*nr), .04*j/(2*nz), 0)
    elements = []
    for j in range(nz):
        for i in range(nr):
            ii, jj = 2*i, 2*j
            offsets = [(0,0),(2,0),(2,2),(0,2),(1,0),(2,1),(1,2),(0,1)]
            elements.append([lookup[ii+di,jj+dj] for di,dj in offsets])
    bottom = [value for (i,j), value in lookup.items() if j == 0]
    top = [value for (i,j), value in lookup.items() if j == 2*nz]
    platen = []
    plate_elements = ""
    surfaces = ""
    if contact:
        for r, y in [(.001,.04),(.04,.04),(.04,.045),(.001,.045),(.0205,.04),(.04,.0425),(.0205,.045),(.001,.0425)]:
            key = len(nodes)+1
            nodes[key] = (r,y,0)
            platen.append(key)
        plate_id = len(elements)+1
        plate_elements = "*ELEMENT,TYPE=CAX8R,ELSET=PLATE\n"+str(plate_id)+","+",".join(map(str,platen))+"\n*SOLID SECTION,ELSET=PLATE,MATERIAL=FIXTURE\n*MATERIAL,NAME=FIXTURE\n*ELASTIC\n2.E11,.3\n"
        faces = "\n".join(f"{(nz-1)*nr+i+1},S3" for i in range(nr))
        surfaces = f"""*SURFACE,NAME=FOAMTOP
{faces}
*SURFACE,NAME=PLATEBOTTOM
{plate_id},S1
*SURFACE INTERACTION,NAME=CONTACT
*SURFACE BEHAVIOR,PRESSURE-OVERCLOSURE=LINEAR
1.E12
*CONTACT PAIR,INTERACTION=CONTACT,TYPE=SURFACE TO SURFACE
FOAMTOP,PLATEBOTTOM
"""
    node_text = "\n".join(str(key)+","+",".join(f"{v:.16g}" for v in point) for key,point in nodes.items())
    element_text = "\n".join(str(i+1)+","+",".join(map(str,element)) for i,element in enumerate(elements))
    load_set = "PLATEN" if contact else "TOP"
    deck = f"""*HEADING
Synthetic annular axisymmetric coupon; verify whole-part RF convention against independent energy derivative
*NODE
{node_text}
*ELEMENT,TYPE=CAX8R,ELSET=FOAM
{element_text}
*MATERIAL,NAME=EDUCATIONAL
*HYPERELASTIC,NEO HOOKE
500000.,2.E-8
*SOLID SECTION,ELSET=FOAM,MATERIAL=EDUCATIONAL
{plate_elements}*NSET,NSET=BOTTOM
{ids(bottom)}
*NSET,NSET=TOP
{ids(top)}
"""
    if contact:
        deck += "*NSET,NSET=PLATEN\n"+",".join(map(str,platen))+"\n"
    deck += surfaces+"*BOUNDARY\nBOTTOM,2,2,0.\n"
    if contact:
        deck += "PLATEN,1,1,0.\n"
    deck += f"""*STEP,NLGEOM,INC=300
*STATIC,SOLVER=SPOOLES
.05,1.,1.E-8,.05
*BOUNDARY
{load_set},2,2,-.012
*NODE PRINT,NSET={load_set},TOTALS=YES
RF
*NODE PRINT,NSET=BOTTOM,TOTALS=YES
RF
*NODE FILE
U
*END STEP
"""
    return deck, load_set


def axisymmetric() -> dict:
    expected = neo_hooke_nominal(.7)*math.pi*(.02**2-.004**2)
    results = []
    scale = None
    for contact in (False, True):
        for nr, nz in [(2,4),(4,8),(8,16)]:
            folder = OUT / "axisymmetric" / f"{'contact' if contact else 'direct'}-{nr}-{nz}"
            deck, load_set = axis_deck(nr, nz, contact)
            report = invoke(folder, deck)
            top = totals(folder/"case.dat", load_set)
            bottom = totals(folder/"case.dat", "BOTTOM")
            if not top or abs(top[-1][0]-1)>1e-8 or not bottom:
                raise ValueError("Axisymmetric run did not finish")
            raw = -top[-1][1][1]
            if scale is None:
                # Only documented full circumference or the 2-degree expansion are candidates.
                scale = min((1.,180.), key=lambda s: abs(raw*s-expected))
            force = raw*scale
            imbalance = abs(top[-1][1][1]+bottom[-1][1][1])*scale
            report.update(radialElements=nr, axialElements=nz, contact=contact, rawReactionN=raw, wholePartScale=scale,
                          wholePartForceN=force, expectedForceN=expected, relativeAnalyticError=abs(force-expected)/expected,
                          imbalanceN=imbalance)
            if abs(force-expected)/expected>.01 or imbalance/expected>.01:
                raise ValueError("Axisymmetric force convention/analytic gate failed: "+json.dumps(report))
            (folder/"verification.json").write_text(json.dumps(report,indent=2))
            results.append(report)
    return {"status":"Verified annular CAX8R whole-part reaction convention with/without platen contact; no fold/locking claim", "results":results}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--case", choices=["coupon","axisymmetric","step","mesh-convergence"], default="coupon")
    parser.add_argument("--mesh",choices=["0_0060","0_0045","0_0035"],default="0_0060")
    parser.add_argument("--compression",type=float,default=.3)
    parser.add_argument("--fold-contact",action="store_true")
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    result = coupon() if args.case == "coupon" else axisymmetric() if args.case=="axisymmetric" else mesh_convergence(args.compression) if args.case=="mesh-convergence" else step_compression(args.mesh,args.compression,args.fold_contact)
    (OUT / f"{args.case}-verification.json").write_text(json.dumps(result, indent=2))
    print(json.dumps({"case": args.case, "status": result["status"], "forcesN": [r.get("forceN",r.get("wholePartForceN")) for r in result["results"]]}))
