"""Bounded, coarse STEP -> FEBio -> actual force curve product adapter.

Single-mesh estimates are deliberately separate from research verification.
No partial solve, fitted analytic substitute, smoothing or extrapolation is used.
"""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import threading
import time
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
def module(name,file):
    spec=importlib.util.spec_from_file_location(name,ROOT/"tools"/file)
    value=importlib.util.module_from_spec(spec);spec.loader.exec_module(value);return value
febio=module("studio_febio","fea-febio-spike.py")
volume=module("studio_volume","fea-volume-prepare.py")

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def make_model(recipe,prepared,steps):
    nodes={int(n):tuple(p) for n,p in prepared["nodes"].items()}
    elements={int(n):list(v) for n,v in prepared["tetrahedra"].items()}
    if len(elements)>20000: raise ValueError("Coarse preview exceeded 20000 tetrahedra. Use a larger mesh size or simpler geometry.")
    fixtures={f["id"]:f for f in recipe["fixtures"]}
    contacts={c["rigidFixtureId"]:c for c in recipe["contacts"] if c["rigidFixtureId"] is not None}
    if set(contacts)-{"lower","upper","shaft"} or not {"lower","upper"}<=set(contacts): raise ValueError("Preview needs lower/upper platen contacts and an optional shaft")
    if not contacts["lower"]["bonded"]: raise ValueError("This first coarse workflow requires a bonded lower mounting face. Enable 'Bond lower selected faces'.")
    def faces(tags):
        result=[]
        for tag in tags:
            result.extend((f["elementId"],f["side"]) for f in prepared["cadFaces"][str(tag)])
        if not result: raise ValueError("A selected contact surface has no mapped volume facets")
        return result
    def face_nodes(items): return sorted({elements[e][i] for e,side in items for i in febio.ccx.TET_FACES[side-1]})
    bottom=faces(contacts["lower"]["slaveFaceTags"]);top=faces(contacts["upper"]["slaveFaceTags"])
    lower=volume.local(volume.vector(fixtures["lower"]["frameInCad"]["originM"]),recipe["compressionFrameInCad"])
    upper=volume.local(volume.vector(fixtures["upper"]["frameInCad"]["originM"]),recipe["compressionFrameInCad"])
    if abs(lower[2])>1e-6 or abs(upper[2]-recipe["freeLengthM"])>1e-6:
        raise ValueError("Use zero initial fixture gaps for the first coarse workflow; interference installation is not yet supported")
    if max(abs(nodes[n][2]) for n in face_nodes(bottom))>2e-6 or max(abs(nodes[n][2]-upper[2]) for n in face_nodes(top))>2e-6:
        raise ValueError("Selected lower/upper faces must be planar at zero/free length in the compression frame. Use the end-face setup action or adjust orientation.")
    groups={"FOAM":("C3D10",elements)};sets={"BOTTOM":face_nodes(bottom),"TOP":face_nodes(top)}
    surfaces={"FOAMTOP":top};lookup={};next_element=max(elements)+1
    def node(point):
        point=tuple(point)
        if point not in lookup: lookup[point]=max(nodes)+1;nodes[lookup[point]]=point
        return lookup[point]
    def polar(radius,q,count,z,origin):
        a=2*math.pi*(q%count)/count;return origin[0]+radius*math.cos(a),origin[1]+radius*math.sin(a),z
    def fixture_group(name,fixture,origin,shaft=False):
        nonlocal next_element
        axis=volume.vector(fixture["frameInCad"]["axis"]);zaxis=volume.basis(recipe["compressionFrameInCad"])[1][2]
        if sum(a*b for a,b in zip(axis,zaxis))<1-1e-8: raise ValueError("Coarse preview requires fixtures aligned to the compression axis")
        count=32 if shaft else 24;inner=0 if shaft else fixture["innerRadiusM"];outer=fixture["outerRadiusM"]
        z=origin[2];height=fixture["lengthM"] if shaft else max(.002,.1*recipe["freeLengthM"])
        group={};bottom_faces=[];side_faces=[]
        for q in range(count):
            if inner==0:
                base=[(origin[0],origin[1],z),polar(outer,q,count,z,origin),polar(outer,q+1,count,z,origin)];kind="C3D6";side=4
            else:
                base=[polar(inner,q,count,z,origin),polar(outer,q,count,z,origin),polar(outer,q+1,count,z,origin),polar(inner,q+1,count,z,origin)];kind="C3D8";side=4
            group[next_element]=[node(p) for p in base+[(x,y,z+height) for x,y,_ in base]]
            bottom_faces.append((next_element,1));side_faces.append((next_element,side));next_element+=1
        groups[name]=(kind,group)
        return sorted({n for ns in group.values() for n in ns}),side_faces if shaft else bottom_faces
    sets["PLATEN"],surfaces["PLATEBOTTOM"]=fixture_group("PLATE",fixtures["upper"],upper)
    if contacts["upper"]["bonded"]: sets["PLATEN"]=sorted(set(sets["PLATEN"]+sets["TOP"]))
    if "shaft" in contacts:
        if contacts["shaft"]["bonded"]: raise ValueError("A bonded shaft is unsupported; select sliding shaft contact")
        f=fixtures["shaft"];origin=volume.local(volume.vector(f["frameInCad"]["originM"]),recipe["compressionFrameInCad"])
        sets["SHAFTNODES"],surfaces["GUIDE"]=fixture_group("SHAFT",f,origin,True)
        surfaces["BORE"]=faces(contacts["shaft"]["slaveFaceTags"])
    folds=[c for c in recipe["contacts"] if c["rigidFixtureId"] is None]
    if len(folds)>1: raise ValueError("Coarse preview supports one explicitly selected fold-contact pair")
    if folds:
        if folds[0]["bonded"]: raise ValueError("Fold contact must allow separation and sliding")
        surfaces["GROOVEUPPER"]=faces(folds[0]["slaveFaceTags"]);surfaces["GROOVELOWER"]=faces(folds[0]["masterFaceTags"])
    model=ET.fromstring(febio.make_model(nodes,groups,sets,surfaces,recipe["requestedCompressionM"]/recipe["freeLengthM"],recipe["freeLengthM"],increment=1/steps,penalty=1e11,max_updates=5,opt_iterations=10,guarded_line_search=True))
    law=recipe["material"].get("law");material=model.find("Material/material[@id='1']")
    if law is None: raise ValueError("Select an estimated material preset or documented compatible material law")
    key=law["modelKey"];parameters=law["parametersSi"]
    material.clear();material.attrib.update(id="1",name="BumperMaterial",type="Mooney-Rivlin" if key=="febio.mooney-rivlin-pressure2.v1" else "Ogden unconstrained")
    if key=="febio.mooney-rivlin-pressure2.v1":
        if parameters["c1"]<=0 or parameters["c2"]<0 or parameters["k"]<=0: raise ValueError("Invalid Mooney-Rivlin coefficients")
        for name in ("c1","c2","k"): febio.add(material,name,parameters[name])
        febio.add(material,"pressure_model",2)
    elif key=="febio.ogden-unconstrained.v1":
        if parameters["cp"]<=0 or parameters["c1"]<=0 or abs(parameters["m1"])<1e-8: raise ValueError("Invalid compressible foam coefficients")
        for name in ("cp","c1","m1"): febio.add(material,name,parameters[name])
    else: raise ValueError("Unsupported material law: "+key)
    model.find("MeshDomains/SolidDomain[@name='FOAM']").set("mat","BumperMaterial")
    # This is an explicitly coarse numerical profile, not the research gate.
    model.find("Control/solver/dtol").text="1e-7";model.find("Control/solver/etol").text="1e-7"
    model.find("Control/solver/rtol").text="1e-7"
    for item in model.findall("Contact/contact"):
        name=item.attrib["surface_pair"]
        pair=contacts["upper"] if name=="TOPCONTACT" else contacts["shaft"] if name=="SHAFTCONTACT" else folds[0]
        item.find("fric_coeff").text=str(pair["frictionCoefficient"])
    ET.indent(model)
    return '<?xml version="1.0" encoding="UTF-8"?>\n'+ET.tostring(model,encoding="unicode"),sets,len(elements)

def run(args):
    folder=args.job.resolve();folder.mkdir(parents=True,exist_ok=True)
    if (folder/"status.json").exists(): raise ValueError("A job folder may only be executed once")
    start=time.perf_counter();lock=threading.Lock();state={"state":"preparing","fraction":0.,"message":"Preparing coarse volume mesh","quality":"CoarseEstimate","curveAvailable":False};stop=threading.Event()
    def write(**changes):
        with lock:
            state.update(changes,elapsedSeconds=time.perf_counter()-start,updatedAtUtc=datetime.now(timezone.utc).isoformat())
            febio.ccx.write_json_atomic(folder/"status.json",dict(state))
    write()
    try:
        recipe=json.loads(args.recipe.read_text());inspection=json.loads(args.inspection.read_text())
        if recipe.get("quality")!="coarseEstimate": raise ValueError("This adapter requires an explicitly CoarseEstimate recipe")
        if recipe["threadCount"]!=4 or recipe["memoryBudgetBytes"]!=2*1024**3: raise ValueError("This adapter supports the fixed four-thread / 2-GB worker profile")
        if recipe["material"].get("readiness")=="needsCalibration": raise ValueError("Select a material with estimated or calibrated coefficients")
        if recipe["requestedCompressionM"]/recipe["freeLengthM"]>recipe["material"]["validity"]["maximumCompressionStrain"]+1e-12: raise ValueError("Requested shortening exceeds this material card's exploration interval; shorten the requested stroke")
        for path,name in ((args.recipe,"recipe.json"),(args.inspection,"inspection.json"),(args.source,"source.step")):
            (folder/name).write_bytes(path.read_bytes())
        for file in ("bump-studio-worker.py","fea-volume-prepare.py","fea-febio-spike.py","fea-plot-diagnostic.py","fea-result-export.py"):
            (folder/("source-"+file)).write_bytes((ROOT/"tools"/file).read_bytes())
        if (folder/"cancel.request").exists(): raise febio.ccx.JobCancelled("Canceled before meshing")
        mesh=volume.prepare(folder/"recipe.json",folder/"inspection.json",folder/"source.step",args.mesh_size,folder/"mesh",folder/"cancel.request",min(90,args.budget_seconds))
        prepared=json.loads((folder/"mesh/volume.json").read_text())
        model,sets,tets=make_model(recipe,prepared,args.steps)
        if (folder/"cancel.request").exists(): raise febio.ccx.JobCancelled("Canceled after meshing")
        remaining=args.budget_seconds-(time.perf_counter()-start)
        if remaining<=0: raise TimeoutError("Coarse job exhausted its time budget during meshing")
        write(state="solving",message="Running coarse nonlinear compression",nodes=len(prepared["nodes"]),tetrahedra=tets)
        solve=folder/"solve"
        def monitor():
            while not stop.wait(.5):
                if (folder/"cancel.request").exists() and solve.exists(): (solve/"cancel.request").write_text("Canceled from Studio")
                try:
                    status=json.loads((solve/"process.json").read_text())
                    write(fraction=status.get("lastConvergedNormalizedTime",0),message="Solving compression",nativePid=status.get("pid"))
                except (OSError,ValueError): pass
        watcher=threading.Thread(target=monitor,daemon=True);watcher.start()
        try: native=febio.invoke(solve,model,timeout=remaining)
        finally: stop.set();watcher.join(timeout=3)
        if (folder/"cancel.request").exists(): raise febio.ccx.JobCancelled("Canceled before result import")
        write(state="reading_results",fraction=1,message="Reading actual reactions and deformation")
        histories={name:febio.history(solve/(name+".txt")) for name in ("PLATEN","BOTTOM","SHAFTNODES") if name in sets}
        top=histories["PLATEN"]
        if len(top)<2 or top[0]["time"]!=0 or abs(top[-1]["time"]-1)>1e-8: raise ValueError("The full requested stroke did not finish; no curve is returned")
        curve=[];imbalance=0
        for i,frame in enumerate(top):
            t=frame["time"]
            if not math.isfinite(t) or (i and t<=top[i-1]["time"]): raise ValueError("Unordered native reaction time")
            total=[0.,0.,0.]
            for name,records in histories.items():
                if len(records)!=len(top) or records[i]["time"]!=t: raise ValueError("Support reaction histories disagree")
                rows=records[i]["values"]
                if {int(r[0]) for r in rows}!=set(sets[name]) or len(rows)!=len(sets[name]) or any(len(r)!=4 or not all(math.isfinite(v) for v in r) for r in rows): raise ValueError("Missing or invalid native reaction node")
                for axis in range(3): total[axis]+=sum(row[axis+1] for row in rows)
            force=sum(row[3] for row in frame["values"])
            if force<0 or i==0 and force!=0: raise ValueError("Compression curve has a negative reaction or nonzero contact origin")
            imbalance=max(imbalance,math.sqrt(sum(v*v for v in total)))
            curve.append({"compressionM":t*recipe["requestedCompressionM"],"forceN":force})
        if curve[-1]["forceN"]<=0 or imbalance>max(1e-4,.05*max(p["forceN"] for p in curve)): raise ValueError("Coarse support balance is insufficient; no curve is returned")
        febio.validation_status(solve,"estimated_complete",curveAccepted=False,quality="CoarseEstimate")
        exporter=module("studio_export","fea-result-export.py")
        exporter.export(solve,folder/"diagnostic-result.json")
        csv="compression_m,force_n\n"+"".join(f"{p['compressionM']:.17g},{p['forceN']:.17g}\n" for p in curve)
        (folder/"curve.csv").write_bytes(csv.encode())
        result={"schemaVersion":"1.0","quality":"CoarseEstimate","recipeSha256":sha(folder/"recipe.json"),"stepSha256":sha(folder/"source.step"),"meshSha256":mesh["volumeSha256"],"solverSha256":native["solverSha256"],"csvSha256":sha(folder/"curve.csv"),"curve":curve,"nodes":len(prepared["nodes"]),"tetrahedra":tets,"maximumSupportImbalanceN":imbalance,"seconds":time.perf_counter()-start}
        (folder/"result.json").write_text(json.dumps(result,indent=2))
        write(state="completed",fraction=1,message="Coarse estimated force curve ready",curveAvailable=True)
        print(json.dumps({"state":"completed","folder":str(folder),"samples":len(curve),"forceN":curve[-1]["forceN"]}),flush=True)
        return result
    except Exception as error:
        stop.set();canceled=(folder/"cancel.request").exists() or isinstance(error,febio.ccx.JobCancelled)
        write(state="canceled" if canceled else "failed",message=str(error),curveAvailable=False)
        raise

if __name__=="__main__":
    p=argparse.ArgumentParser();p.add_argument("--recipe",required=True,type=Path);p.add_argument("--inspection",required=True,type=Path);p.add_argument("--source",required=True,type=Path);p.add_argument("--job",required=True,type=Path);p.add_argument("--mesh-size",type=float,default=None);p.add_argument("--steps",type=int,default=8);p.add_argument("--budget-seconds",type=int,default=180)
    a=p.parse_args()
    if not 2<=a.steps<=100 or not 1<=a.budget_seconds<=28800 or a.mesh_size is not None and (not math.isfinite(a.mesh_size) or a.mesh_size<=0): p.error("Use 2–100 steps, a positive mesh size and a bounded time budget")
    run(a)
