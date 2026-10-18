"""Pinned FEBio comparison of the synthetic CalculiX cases, not a production backend.

Uses the same prepared quadratic mesh and fully prescribed fixture nodes. SI units.
FEBio Mooney-Rivlin c2=0, pressure_model=2 exactly matches the CCX NH energy.
"""
from __future__ import annotations

import argparse
import ctypes
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET
import uuid

import psutil

ROOT = Path(__file__).resolve().parents[1]
EXE = Path(os.environ.get("SUSPENSION_FEBIO_EXE", r"D:\Program Files\FEBioStudio\bin\febio4.exe"))
EXE_SHA = "7bee81e57fea75371f9e45449aad525c24ae26e6b9f24bc33f2c35dcf896b0e2"
OUT = ROOT / "artifacts/febio-verification"
spec = importlib.util.spec_from_file_location("ccx_benchmark", ROOT / "tools/fea-contact-spike.py")
ccx = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ccx)


def add(parent, tag, value=None, **attributes):
    item = ET.SubElement(parent, tag, attributes)
    if value is not None:
        item.text = str(value)
    return item


def parse_ccx(deck):
    nodes, groups, sets, surfaces = {}, {}, {}, {}
    mode = None
    for line in deck.splitlines():
        if line.startswith("*"):
            fields = line.upper().split(",")
            options = dict(field.split("=", 1) for field in fields[1:] if "=" in field)
            mode = fields[0]
            if mode == "*ELEMENT":
                name = options["ELSET"]
                kind, values = groups.setdefault(name, (options["TYPE"], {}))
            elif mode == "*NSET":
                values = sets.setdefault(options["NSET"], [])
            elif mode == "*SURFACE":
                values = surfaces.setdefault(options["NAME"], [])
        elif line.strip():
            fields = line.split(",")
            if mode == "*NODE":
                nodes[int(fields[0])] = tuple(map(float, fields[1:4]))
            elif mode == "*ELEMENT":
                values[int(fields[0])] = list(map(int, fields[1:]))
            elif mode == "*NSET":
                values.extend(map(int, fields))
            elif mode == "*SURFACE":
                values.append((int(fields[0]), int(fields[1].lstrip("S"))))
    return nodes, groups, sets, surfaces


def outward_face(nodes, kind, vertices, face):
    patterns = {
        "C3D10": ccx.TET_FACES,
        "C3D8": [(0,1,2,3),(4,5,6,7),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],
        "C3D6": [(0,1,2),(3,4,5),(0,3,4,1),(1,4,5,2),(2,5,3,0)]}
    indices = [vertices[i] for i in patterns[kind][face-1]]
    corners = 3 if len(indices) in (3,6) else 4
    p = [nodes[n] for n in indices[:corners]]
    a, b = [[p[k][i]-p[0][i] for i in range(3)] for k in (1,2)]
    normal = (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
    element_corners = vertices[:4] if kind=="C3D10" else vertices
    center = [sum(nodes[n][i] for n in element_corners)/len(element_corners) for i in range(3)]
    if sum(normal[i]*(center[i]-p[0][i]) for i in range(3)) > 0:
        order = [0,2,1,5,4,3] if len(indices)==6 else [0,2,1] if len(indices)==3 else [0,3,2,1]
        indices = [indices[i] for i in order]
    return {3:"tri3",4:"quad4",6:"tri6"}[len(indices)], indices


def make_model(nodes, groups, sets, surfaces, fraction, height, coupon=False, contact=True, increment=.025, penalty=1e12, max_updates=0, opt_iterations=10, guarded_line_search=False, bulk_modulus=1e8, line_search_tolerance=.9):
    root = ET.Element("febio_spec", version="4.0")
    add(root, "Module", type="solid")
    control = add(root, "Control")
    add(control,"analysis","STATIC")
    add(control,"time_steps",round(1/increment)); add(control,"step_size",increment)
    solver = add(control,"solver")
    for key,value in {"max_refs":35,"diverge_reform":1,"reform_each_time_step":1,"symmetric_stiffness":0,"dtol":1e-5,"etol":1e-5,"rtol":0,"lstol":line_search_tolerance}.items():
        add(solver,key,value)
    if guarded_line_search:
        # FEBio can backtrack an inverted trial configuration instead of cutting
        # the entire load increment. Accepted-state tolerances stay unchanged.
        for key,value in {"ls_check_jacobians":1,"lsmin":1e-5,"lsiter":20,"reform_augment":1}.items():
            add(solver,key,value)
    qn=add(solver,"qn_method",type="Broyden");add(qn,"max_ups",max_updates)
    stepper = add(control,"time_stepper")
    for key,value in {"dtmin":1e-7,"dtmax":increment,"max_retries":15,"opt_iter":opt_iterations}.items():
        add(stepper,key,value)
    materials = add(root,"Material")
    foam = add(materials,"material",id="1",name="SyntheticNH",type="Mooney-Rivlin")
    for key,value in {"c1":500000,"c2":0,"k":bulk_modulus,"pressure_model":2}.items():
        add(foam,key,value)
    fixture = add(materials,"material",id="2",name="PrescribedFixture",type="isotropic elastic")
    add(fixture,"E",2e11); add(fixture,"v",.3)
    mesh = add(root,"Mesh")
    mesh_nodes = add(mesh,"Nodes",name="AllNodes")
    for n,p in nodes.items():
        add(mesh_nodes,"node",",".join(f"{x:.12g}" for x in p),id=str(n))
    types={"C3D10":"tet10","C3D8":"hex8","C3D6":"penta6"}
    lookup={}
    for name,(kind,elements) in groups.items():
        group=add(mesh,"Elements",name=name,type=types[kind])
        for e,vertices in elements.items():
            add(group,"elem",",".join(map(str,vertices)),id=str(e));lookup[e]=(kind,vertices)
    for name,values in sets.items():
        add(mesh,"NodeSet",",".join(map(str,values)),name=name)
    for name,faces in surfaces.items():
        surface=add(mesh,"Surface",name=name)
        for i,(element,face) in enumerate(faces,1):
            kind,vertices=lookup[element]
            shape,indices=outward_face(nodes,kind,vertices,face)
            add(surface,shape,",".join(map(str,indices)),id=str(i))
    pairs=[]
    if contact:
        pairs=[("TOPCONTACT","FOAMTOP","PLATEBOTTOM")]
        if "BORE" in surfaces:
            pairs.append(("SHAFTCONTACT","BORE","GUIDE"))
        if "GROOVEUPPER" in surfaces:
            pairs.append(("FOLDCONTACT","GROOVEUPPER","GROOVELOWER"))
    for name,primary,secondary in pairs:
        pair=add(mesh,"SurfacePair",name=name)
        add(pair,"primary",primary); add(pair,"secondary",secondary)
    domains=add(root,"MeshDomains")
    for name in groups:
        add(domains,"SolidDomain",name=name,mat="SyntheticNH" if name=="FOAM" else "PrescribedFixture")
    boundary=add(root,"Boundary")
    def fix(name,axes):
        # FEBio excludes fixed dofs from its nodal reaction record. Prescribed
        # exact zero retains identical kinematics and makes support sums observable.
        for axis in axes:
            bc=add(boundary,"bc",type="prescribed displacement",node_set=name)
            add(bc,"dof",axis);add(bc,"value",0,lc="1");add(bc,"relative",0)
    def prescribe(name,distance):
        bc=add(boundary,"bc",type="prescribed displacement",node_set=name)
        add(bc,"dof","z");add(bc,"value",distance,lc="1");add(bc,"relative",0)
    fix("BOTTOM","z" if coupon else "xyz")
    if coupon:
        fix("PINXY","xy");fix("PINY","y")
    if contact:
        fix("PLATEN","xy");prescribe("PLATEN",-fraction*height)
        if "SHAFTNODES" in sets: fix("SHAFTNODES","xyz")
    else:
        prescribe("TOP",-fraction*height)
    if pairs:
        contacts=add(root,"Contact")
        for name,_,_ in pairs:
            item=add(contacts,"contact",type="sliding-elastic",surface_pair=name)
            for key,value in {"laugon":1,"tolerance":.01,"gaptol":1e-6,"penalty":penalty,"auto_penalty":0,"two_pass":0,"search_radius":.05,"symmetric_stiffness":0,"maxaug":20,"fric_coeff":0,"tension":0}.items():
                add(item,key,value)
    load=add(root,"LoadData");lc=add(load,"load_controller",id="1",type="loadcurve")
    add(lc,"interpolate","LINEAR");points=add(lc,"points")
    add(points,"pt","0,0");add(points,"pt","1,1")
    output=add(root,"Output");log=add(output,"logfile")
    for name in ("PLATEN" if contact else "TOP","BOTTOM","SHAFTNODES"):
        if name in sets: add(log,"node_data",",".join(map(str,sets[name])),data="Rx;Ry;Rz",file=name+".txt")
    for name,primary,_ in pairs:
        add(log,"face_data",data="contact gap;contact pressure",surface=primary,file=name+".txt")
        add(log,"surface_data",data="max contact gap",surface=primary,file=name+"-MAXGAP.txt")
    plot=add(output,"plotfile",type="febio",file="case.xplt")
    for name in ("displacement","stress","relative volume","contact pressure","contact gap"):
        add(plot,"var",type=name)
    ET.indent(root)
    return '<?xml version="1.0" encoding="UTF-8"?>\n'+ET.tostring(root,encoding="unicode")


def invoke(folder, model, timeout=240, cancel_after=None):
    pin_path=ROOT/"data/fea/febio-pin.json"
    pin=json.loads(pin_path.read_text(encoding="utf-8-sig"))
    if hashlib.sha256(EXE.read_bytes()).hexdigest()!=EXE_SHA or pin["executableSha256"]!=EXE_SHA:
        raise ValueError("FEBio executable differs from the reviewed pin")
    for name,sha in pin["libraries"].items():
        if hashlib.sha256((EXE.parent/name).read_bytes()).hexdigest()!=sha:
            raise ValueError("FEBio dependency differs from its reviewed pin: "+name)
    folder.mkdir(parents=True,exist_ok=True)
    verified=folder/"verification.json"
    if verified.exists(): verified.unlink()
    (folder/"case.feb").write_bytes(model.encode("utf-8"))
    cancel=folder/"cancel.request"
    if cancel.exists(): cancel.unlink()
    env=dict(os.environ,OMP_NUM_THREADS="4",MKL_NUM_THREADS="4",OPENBLAS_NUM_THREADS="4")
    state={"state":"running","solverSha256":EXE_SHA,"dependencyPinSha256":hashlib.sha256(pin_path.read_bytes()).hexdigest(),"modelSha256":hashlib.sha256(model.encode()).hexdigest(),"threads":4,"memoryLimitBytes":2*1024**3,"timeoutSeconds":timeout,
           "folder":str(folder),"inputFileSha256":hashlib.sha256((folder/"case.feb").read_bytes()).hexdigest(),
           "startedAtUtc":datetime.now(timezone.utc).isoformat(),"curveAccepted":False}
    start=time.perf_counter();peak=0;next_heartbeat=0
    def heartbeat():
        state.update(seconds=time.perf_counter()-start,peakWorkingSetBytes=peak,updatedAtUtc=datetime.now(timezone.utc).isoformat())
        for name in ("PLATEN.txt","TOP.txt"):
            path=folder/name
            if path.exists():
                # Read only a bounded tail; long jobs can generate very large files.
                with path.open("rb") as stream:
                    stream.seek(max(0,path.stat().st_size-131072))
                    tail=stream.read().decode(errors="replace")
                records=re.findall(r"\*Time\s*=\s*([-+\d.Ee]+)",tail)
                if records: state["lastConvergedNormalizedTime"]=float(records[-1])
                break
        ccx.journal(folder,state)
        ccx.write_json_atomic(OUT/"current-job.json",state)
    with (folder/"stdout.log").open("w") as stdout,(folder/"stderr.log").open("w") as stderr:
        previous=ctypes.windll.kernel32.SetErrorMode(0x0001|0x0002|0x8000)
        try:
            process=subprocess.Popen([str(EXE),"-i",str(folder/"case.feb"),"-noconfig","-nosplash"],cwd=folder,env=env,stdout=stdout,stderr=stderr,creationflags=subprocess.CREATE_NO_WINDOW)
        finally: ctypes.windll.kernel32.SetErrorMode(previous)
        state["pid"]=process.pid;monitor=psutil.Process(process.pid)
        try:
            heartbeat()
            while process.poll() is None:
                try: peak=max(peak,monitor.memory_info().rss)
                except psutil.NoSuchProcess: pass
                if time.perf_counter()-start>timeout or peak>2*1024**3: raise TimeoutError("FEBio time/memory budget exceeded")
                if cancel.exists() or (cancel_after is not None and time.perf_counter()-start>cancel_after):
                    raise ccx.JobCancelled("FEBio canceled; no curve accepted")
                if time.perf_counter()>=next_heartbeat:
                    heartbeat();next_heartbeat=time.perf_counter()+5
                time.sleep(.05)
            log=(folder/"stdout.log").read_text(errors="replace")
            if process.returncode or "N O R M A L   T E R M I N A T I O N" not in log or "Default linear solver: pardiso" not in log:
                raise RuntimeError(f"FEBio did not finish cleanly (exit {process.returncode}); inspect {folder}")
            state.update(state="solver_succeeded",exitCode=process.returncode)
        except Exception as error:
            state.update(state="canceled" if isinstance(error,ccx.JobCancelled) else "failed",error=str(error),exitCode=process.poll())
            raise
        finally:
            if process.poll() is None:
                try:
                    for child in monitor.children(recursive=True): child.kill()
                    process.kill()
                except psutil.NoSuchProcess: pass
                process.wait()
            heartbeat()
    return state


def history(path):
    result=[];current=None
    for line in path.read_text().splitlines():
        match=re.search(r"Time\s*=\s*([-+\d.Ee]+)",line)
        if match:
            current={"time":float(match[1]),"values":[]};result.append(current)
        elif current is not None and line.strip() and not line.startswith("*"):
            try: fields=list(map(float,line.split()))
            except ValueError: continue
            if len(fields)>=2: current["values"].append(fields)
    return result


def coupon(contact=False):
    deck=ccx.cube_contact(1e12,.025)
    nodes,groups,sets,surfaces=parse_ccx(deck)
    sets["PINXY"]=[1];sets["PINY"]=[2]
    if not contact:
        groups={"FOAM":groups["FOAM"]};nodes={n:p for n,p in nodes.items() if n<=8}
        sets={n:v for n,v in sets.items() if n!="PLATEN"};surfaces={}
    return nodes,groups,sets,surfaces


def verify_results(folder, report, metadata, args, sets):
    top_name="TOP" if args.case=="coupon" else "PLATEN"
    top=history(folder/(top_name+".txt"))
    support_names=[name for name in ("BOTTOM","SHAFTNODES") if name in sets]
    supports={name:history(folder/(name+".txt")) for name in support_names}
    histories={top_name:top,**supports}
    for name,records in histories.items():
        if not records or abs(records[-1]["time"]-1)>1e-8:
            raise ValueError("Missing complete reaction history for "+name)
        for record in records:
            ids=[int(row[0]) for row in record["values"]]
            if len(ids)!=len(set(ids)) or set(ids)!=set(sets[name]) or any(len(row)!=4 or not all(math.isfinite(v) for v in row) for row in record["values"]):
                raise ValueError("Incomplete, duplicated or non-finite nodal reaction record: "+name)
    force=sum(v[3] for v in top[-1]["values"])
    scale=max(abs(sum(v[3] for v in record["values"])) for record in top)
    if force<=0: raise ValueError("Final compression reaction is not positive")
    maximum_imbalance=0
    for i,record in enumerate(top):
        total=[sum(row[axis+1] for row in record["values"]) for axis in range(3)]
        for name,records in supports.items():
            if len(records)!=len(top) or abs(records[i]["time"]-record["time"])>1e-9:
                raise ValueError("Reaction histories disagree on accepted times: "+name)
            for axis in range(3): total[axis]+=sum(row[axis+1] for row in records[i]["values"])
        maximum_imbalance=max(maximum_imbalance,math.sqrt(sum(value*value for value in total)))
    if maximum_imbalance>max(1e-5,.01*scale):
        raise ValueError("Full-history resultant support balance gate failed")
    report.update(metadata,forceN=force,imbalanceN=maximum_imbalance,history=top,
                  forceScaleN=scale,material=metadata.get("material","C1=500000 Pa; C2=0; K=1e8 Pa; pressure_model=2"),synthetic=True)
    if args.case not in ("step","rubber"):
        expected=ccx.neo_hooke_nominal(1-args.compression)*.02**2
        report.update(expectedForceN=expected,relativeAnalyticError=abs(force-expected)/expected)
        if report["relativeAnalyticError"]>.01: raise ValueError("Independent constitutive/reaction check failed")
    if args.case!="coupon" and not (folder/"TOPCONTACT.txt").exists():
        raise ValueError("Required platen-contact history is missing")
    for name in ("TOPCONTACT","FOLDCONTACT","SHAFTCONTACT"):
        path=folder/(name+".txt")
        if not path.exists(): continue
        records=history(path)
        if not records or abs(records[-1]["time"]-1)>1e-8:
            raise ValueError("Incomplete contact history: "+name)
        values=[v for record in records for v in record["values"]]
        maxima=history(folder/(name+"-MAXGAP.txt"))
        if not maxima or abs(maxima[-1]["time"]-1)>1e-8:
            raise ValueError("Incomplete maximum reported contact gap history: "+name)
        for contact_history in (records,maxima):
            if len(contact_history)!=len(top) or any(abs(a["time"]-b["time"])>1e-9 for a,b in zip(contact_history,top)):
                raise ValueError("Contact and reaction accepted times disagree: "+name)
            expected_ids={row[0] for row in contact_history[0]["values"]}
            if not expected_ids or any(len(frame["values"])!=len(expected_ids) or {row[0] for row in frame["values"]}!=expected_ids for frame in contact_history):
                raise ValueError("Missing, changed or duplicated contact record identities: "+name)
        if any(len(row)!=3 or not all(math.isfinite(value) for value in row) for row in values) or any(len(row)!=2 or not all(math.isfinite(value) for value in row) for record in maxima for row in record["values"]):
            raise ValueError("Invalid or non-finite contact record: "+name)
        maximum_gap=max((abs(v[-1]) for record in maxima for v in record["values"]),default=0)
        report[name]={"maximumPressurePa":max((v[2] for v in values),default=0),
                      "maximumActiveFacetAverageAbsoluteGapM":max((abs(v[1]) for v in values if v[2]>1000),default=0),
                      "maximumReportedContactGapM":maximum_gap}
        if maximum_gap>1e-5: raise ValueError("Maximum contact penetration gate failed: "+name)
    for required,enabled in (("FOLDCONTACT",args.fold_contact),("SHAFTCONTACT",args.guide_shaft)):
        if enabled and report.get(required,{}).get("maximumPressurePa",0)<=1000:
            raise ValueError(required+" did not engage; this case cannot verify its contact behavior")
    return report


def validation_status(folder, state, **details):
    status=json.loads((folder/"process.json").read_text())
    status.update(state=state,updatedAtUtc=datetime.now(timezone.utc).isoformat(),**details)
    ccx.journal(folder,status)
    path=OUT/"current-job.json"
    if path.exists() and json.loads(path.read_text()).get("folder")==str(folder):
        ccx.write_json_atomic(path,status)


def recovery():
    geometry=coupon(False)
    model=make_model(*geometry,.3,.02,coupon=True,contact=False)
    results=[]
    for name,input_model,options,expected in [
        ("bad-input",'<?xml version="1.0"?><febio_spec version="4.0"><invalid/></febio_spec>',{},RuntimeError),
        ("timeout",model,{"timeout":.02},TimeoutError),
        ("cancel",model,{"cancel_after":.02},ccx.JobCancelled)]:
        folder=OUT/"recovery"/name
        try:
            invoke(folder,input_model,**options)
            raise ValueError("Deliberately rejected FEBio job was accepted")
        except expected:
            state=json.loads((folder/"process.json").read_text())
            if psutil.pid_exists(state["pid"]) or (folder/"verification.json").exists():
                raise ValueError("Rejected worker survived or retained verification")
            results.append({"case":name,"state":state["state"],"childCleanedUp":True})
    folder=OUT/"recovery"/"valid-after-failures"
    valid=invoke(folder,model)
    top=history(folder/"TOP.txt")[-1]
    force=sum(v[3] for v in top["values"])
    if abs(top["time"]-1)>1e-8 or abs(force-ccx.neo_hooke_nominal(.7)*.02**2)>.01:
        raise ValueError("Valid FEM job did not recover after failures")
    report={"status":"Invalid input, timeout, cancellation cleanup and subsequent valid solve verified", "results":results,"validForceN":force,"validRun":valid}
    (OUT/"recovery-verification.json").write_text(json.dumps(report,indent=2))
    print(json.dumps(report))


if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--case",choices=["coupon","contact","step","rubber","recovery"],default="coupon")
    parser.add_argument("--compression",type=float,default=.3)
    parser.add_argument("--mesh",choices=["0_0060","0_0045","0_0035"],default="0_0060")
    parser.add_argument("--increment",type=float,default=.025)
    parser.add_argument("--penalty",type=float,default=1e12)
    parser.add_argument("--max-updates",type=int,default=0)
    parser.add_argument("--opt-iterations",type=int,default=10)
    parser.add_argument("--guarded-line-search",action="store_true",help="Backtrack inverted trial states and reform after contact augmentation; unchanged physical model and acceptance tolerances.")
    parser.add_argument("--line-search-tolerance",type=float,default=.9,help="FEBio lstol; zero uses the full Newton step. Accepted displacement/energy/contact thresholds are unchanged.")
    parser.add_argument("--timeout-hours",type=float,default=8,help="Wall-clock budget per solve; default eight hours. Numerical/contact acceptance is unchanged.")
    parser.add_argument("--fold-contact",action="store_true")
    parser.add_argument("--guide-shaft",action="store_true")
    args=parser.parse_args()
    if not 0<args.compression<1 or not 0<args.increment<=1 or not math.isfinite(args.penalty) or args.penalty<=0 or args.max_updates<0 or args.opt_iterations<=0 or not math.isfinite(args.timeout_hours) or not 0<args.timeout_hours<=8 or not math.isfinite(args.line_search_tolerance) or not 0<=args.line_search_tolerance<=1:
        parser.error("Require 0<compression<1, 0<increment<=1, positive finite penalty, nonnegative updates, positive optimal iterations and 0<timeout-hours<=8")
    if args.case=="recovery":
        recovery()
        raise SystemExit(0)
    if args.case=="rubber" and (args.fold_contact or args.guide_shaft or abs(args.compression-.3)>1e-12):
        parser.error("The nearly incompressible reference case declares 30% compression without groove/shaft contact")
    metadata={}
    if args.case in ("step","rubber"):
        nodes,elements,mesh_sha=ccx.step_mesh(args.mesh,"annular" if args.case=="rubber" else "asymmetric")
        quality=ccx.quadratic_quality(nodes,elements)
        deck,metadata=ccx.step_deck(nodes,elements,fraction=args.compression,fold=args.fold_contact,guide=args.guide_shaft)
        nodes,groups,sets,surfaces=parse_ccx(deck)
        metadata.update(meshSha256=mesh_sha,initialMeshQuality=quality,ccxSourceDeckSha256=hashlib.sha256(deck.encode()).hexdigest(),
                        fixture="Identical mesh and prescribed fixture nodes to CCX; bonded base, frictionless platen; optional bore shaft and groove face contact",
                        foldContactEnabled=args.fold_contact,guideShaftEnabled=args.guide_shaft,
                        material="C1=500000 Pa; C2=0; K="+("1e9" if args.case=="rubber" else "1e8")+" Pa; pressure_model=2")
    else:
        nodes,groups,sets,surfaces=coupon(args.case=="contact")
    model=make_model(nodes,groups,sets,surfaces,args.compression,.04 if args.case in ("step","rubber") else .02,coupon=args.case not in ("step","rubber"),contact=args.case!="coupon",increment=args.increment,penalty=args.penalty,max_updates=args.max_updates,opt_iterations=args.opt_iterations,guarded_line_search=args.guarded_line_search,bulk_modulus=1e9 if args.case=="rubber" else 1e8,line_search_tolerance=args.line_search_tolerance)
    key=f"{args.case}-{args.mesh}-{args.compression:.2f}-"+hashlib.sha256(model.encode()).hexdigest()[:12]
    # Each attempt retains its own deck, source code, settings, history and outcome.
    # Raising the budget must never overwrite the previous rejected benchmark.
    folder=OUT/key/("run-"+datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")+"-"+uuid.uuid4().hex[:8])
    folder.mkdir(parents=True)
    (folder/"source-driver.py").write_bytes(Path(__file__).read_bytes())
    (folder/"source-ccx-reference.py").write_bytes((ROOT/"tools/fea-contact-spike.py").read_bytes())
    (folder/"source-pin.json").write_bytes((ROOT/"data/fea/febio-pin.json").read_bytes())
    (folder/"request.json").write_text(json.dumps({"arguments":vars(args),"metadata":metadata,"driverSha256":hashlib.sha256(Path(__file__).read_bytes()).hexdigest()},indent=2))
    print(json.dumps({"state":"starting","folder":str(folder),"timeoutHours":args.timeout_hours}),flush=True)
    report=invoke(folder,model,timeout=args.timeout_hours*3600)
    validation_status(folder,"validating")
    try:
        report=verify_results(folder,report,metadata,args,sets)
        report.update(validationState="verified_for_declared_case_only",curveAccepted=True)
        (folder/"verification.json").write_text(json.dumps(report,indent=2))
        validation_status(folder,"verified",curveAccepted=True,verificationPath=str(folder/"verification.json"))
    except Exception as error:
        validation_status(folder,"validation_failed",curveAccepted=False,error=str(error))
        raise
    print(json.dumps({k:report[k] for k in ("forceN","imbalanceN","seconds","peakWorkingSetBytes")}|{"folder":str(folder)}))
