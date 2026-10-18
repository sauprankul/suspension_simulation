"""Export checked, discrete FEBio benchmark states for Studio's read-only viewer.

Only the pinned reader's nonadaptive v0x35 format and the benchmark's linear
axial loading are supported. No curve publication or backend admission occurs.
Terminal jobs only: a changing writer cannot silently invalidate export hashes.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import re
import xml.etree.ElementTree as ET

import numpy as np

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("checked_plot_reader",ROOT/"tools/fea-plot-diagnostic.py")
reader=importlib.util.module_from_spec(spec);spec.loader.exec_module(reader)


def sha(path):
    digest=hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda:stream.read(1024*1024),b""): digest.update(chunk)
    return digest.hexdigest()


def vector(point):
    return dict(zip(("x","y","z"),map(float,point)))


def boundary(model,known_ids):
    patterns={"tet10":[(0,1,2,4,5,6),(0,3,1,7,8,4),(1,3,2,8,9,5),(2,3,0,9,7,6)],
              "hex8":[(0,1,2,3),(4,5,6,7),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],
              "penta6":[(0,1,2),(3,4,5),(0,3,4,1),(1,4,5,2),(2,5,3,0)]}
    parts=[];triangles=[]
    for group in model.findall("Mesh/Elements"):
        kind=group.attrib["type"];name=group.attrib["name"];faces={}
        if kind not in patterns or name not in ("FOAM","PLATE","SHAFT"):
            raise ValueError("Only the declared benchmark body/fixture elements are supported")
        if any(p["id"]==name for p in parts): raise ValueError("Duplicate element part")
        parts.append({"id":name,"name":"Bumper body" if name=="FOAM" else "Upper platen" if name=="PLATE" else "Guide shaft",
                      "role":"body" if name=="FOAM" else "fixture"})
        for element in group:
            nodes=list(map(int,element.text.split(",")))
            if len(nodes)!=int(re.search(r"\d+",kind)[0]) or not set(nodes)<=known_ids or len(set(nodes))!=len(nodes):
                raise ValueError("Invalid element connectivity")
            for pattern in patterns[kind]:
                face=[nodes[i] for i in pattern];key=tuple(sorted(face[:3] if kind=="tet10" else face))
                faces.setdefault(key,[]).append(face)
        for records in faces.values():
            if len(records)>2: raise ValueError("Nonmanifold element face")
            if len(records)==2:
                if set(records[0])!=set(records[1]): raise ValueError("Nonconforming midside connectivity")
                continue
            face=records[0]
            order=[(0,3,5),(3,1,4),(5,4,2),(3,4,5)] if kind=="tet10" else [(0,1,2),(0,2,3)] if len(face)==4 else [(0,1,2)]
            triangles.extend({"partId":name,"a":face[a],"b":face[b],"c":face[c]} for a,b,c in order)
    if not parts or not triangles or len(triangles)>250000: raise ValueError("Empty/oversize boundary topology")
    return parts,triangles


def linear_loading(model):
    controllers=model.findall("LoadData/load_controller")
    if len(controllers)!=1 or controllers[0].attrib!={"id":"1","type":"loadcurve"} or controllers[0].findtext("interpolate")!="LINEAR":
        raise ValueError("Diagnostic export requires the benchmark's single linear load controller")
    if [tuple(map(float,p.text.split(","))) for p in controllers[0].findall("points/pt")]!=[(0.,0.),(1.,1.)]:
        raise ValueError("Diagnostic export does not infer arbitrary load histories")
    driven=[]
    for bc in model.findall("Boundary/bc"):
        if bc.attrib.get("type")!="prescribed displacement": continue
        value=bc.find("value")
        if value is None or value.attrib!={"lc":"1"} or bc.findtext("relative")!="0":
            raise ValueError("Unsupported prescribed-displacement convention")
        if float(value.text)!=0:
            if bc.findtext("dof")!="z" or float(value.text)>=0: raise ValueError("Only negative-Z axial shortening is supported")
            driven.append((bc.attrib["node_set"],-float(value.text)))
    if len(driven)!=1 or driven[0][0] not in ("TOP","PLATEN"): raise ValueError("Expected one known driven face")
    return driven[0]


def reactions(path,expected_ids):
    records=[];current=None
    for line in path.read_text().splitlines():
        match=re.fullmatch(r"\*Time\s*=\s*([-+\d.Ee]+)\s*",line)
        if match:
            current={"time":float(match[1]),"values":[]};records.append(current)
        elif current is not None and line.strip() and not line.startswith("*"):
            fields=list(map(float,line.split()))
            if len(fields)!=4 or not all(math.isfinite(v) for v in fields) or fields[0]!=int(fields[0]): raise ValueError("Malformed reaction record")
            current["values"].append(fields)
    if not records or len(records)>200000: raise ValueError("Missing/oversize reaction history")
    for i,row in enumerate(records):
        ids=[int(v[0]) for v in row["values"]]
        if len(ids)!=len(set(ids)) or set(ids)!=expected_ids: raise ValueError("Incomplete/duplicated reaction frame")
        if not math.isfinite(row["time"]) or not 0<=row["time"]<=1 or (i and row["time"]<=records[i-1]["time"]):
            raise ValueError("Reaction history is not strictly ordered")
    return np.array([r["time"] for r in records]),np.array([sum(v[3] for v in r["values"]) for r in records])


def export(folder,output=None,maximum_frames=61):
    process_sha=sha(folder/"process.json");state=json.loads((folder/"process.json").read_text())
    if state["state"] not in ("verified","solver_succeeded","estimated_complete","failed","canceled","validation_failed"):
        raise ValueError("Export a terminal job; active results require a separate immutable snapshot")
    if state["state"]=="solver_succeeded" and "inputFileSha256" in state:
        raise ValueError("Wait for the wrapper's terminal verification outcome before exporting")
    plot_sha=sha(folder/"case.xplt");input_sha=sha(folder/"case.feb")
    model=ET.fromstring((folder/"case.feb").read_bytes())
    top,stroke=linear_loading(model)
    node_set=model.find(f"Mesh/NodeSet[@name='{top}']")
    reaction_path=folder/(top+".txt");reaction_sha=sha(reaction_path)
    times,forces=reactions(reaction_path,set(map(int,node_set.text.split(","))))
    if list(times)!=reader.accepted_times(folder): raise ValueError("Reaction time/step identities disagree")
    model,ids,xyz,_,info=reader.read_plot(folder,state.get("lastConvergedNormalizedTime",1),accepted_time_values=times)
    if len(ids)>100000 or len(ids)==0: raise ValueError("Node budget exceeded")
    # Retain the full native reaction curve; only complete plot frames can be shown.
    maximum_time=min(float(times[-1]),info["normalizedTime"])
    _,_,_,_,info=reader.read_plot(folder,maximum_time,accepted_time_values=times)
    indices=({info["completeFramesRead"]-1} if maximum_frames==1 else
             set(np.linspace(0,info["completeFramesRead"]-1,min(maximum_frames,info["completeFramesRead"])).round().astype(int)))
    frames=[];errors=[]
    def keep(index,time_value,displacement):
        if index not in indices: return
        if index>=len(times) or time_value!=times[index]:
            raise ValueError("Plot frame lacks a matching converged reaction record")
        errors.append(reader.check_fixture(model,ids,displacement,time_value))
        frames.append({"normalizedTime":time_value,"compressionM":stroke*time_value,
                       "forceN":float(forces[index]),"displacementsM":[vector(v) for v in displacement]})
    reader.read_plot(folder,maximum_time,on_frame=keep,accepted_time_values=times)
    parts,triangles=boundary(model,set(map(int,ids)))
    complete=bool(abs(frames[-1]["normalizedTime"]-1)<1e-7 and abs(times[-1]-1)<1e-8)
    # Only current explicit verification can claim the declared numerical case.
    # This never means a material is calibrated or a production backend admitted.
    accepted=complete and state["state"]=="verified" and state.get("curveAccepted") is True
    if accepted:
        report=json.loads((folder/"verification.json").read_text())
        if report.get("curveAccepted") is not True or report.get("modelSha256")!=input_sha:
            raise ValueError("Verification manifest does not identify the same accepted input")
        recorded=report["history"]
        if len(recorded)!=len(times) or any(row["time"]!=t or sum(v[3] for v in row["values"])!=f for row,t,f in zip(recorded,times,forces)):
            raise ValueError("Reaction history differs from the declared verification report")
    warnings=["Synthetic numerical benchmark; no measured material or commercial-part correlation.",
              "Discrete converged nodal states at 1× scale. Quadratic faces subdivided for display; solver boundary surfaces, not CAD face IDs.",
              "Diagnostic completion does not admit the production backend or authorize bumper-curve publication."]
    if not complete: warnings.append("Incomplete solve: requested stroke was not reached. Retained converged increments are diagnostic only.")
    if info["legacyWindowsLineEndings"]: warnings.append("Legacy run: input identity checked against the recorded LF-normalized model hash; raw input SHA is retained separately.")
    curve=[{"compressionM":float(t*stroke),"forceN":float(f)} for t,f in zip(times,forces)]
    coarse=state["state"]=="estimated_complete"
    if coarse:
        warnings=[w for w in warnings if "authorize bumper-curve publication" not in w and "Synthetic numerical benchmark" not in w]
        warnings.append("Coarse single-mesh STEP estimate. Mesh convergence and physical calibration have not been established. Studio may create an Estimated component from the separately checked complete job.")
    result={"schema":"suspension-fea/diagnostic-result/v1","label":"Coarse STEP bumper estimate" if coarse else "Synthetic "+("guided grooved bumper" if len(parts)>2 else "compression benchmark"),
            "complete":complete,"curveAccepted":accepted,"synthetic":not coarse,"sourceModelSha256":state["modelSha256"],"inputFileSha256":input_sha,"plotFileSha256":plot_sha,
            "requestedCompressionM":stroke,"conditions":"Axial quasi-static compression; exact material, fixtures and contact settings retained in case.feb. Compression force is the signed sum of driven-face FEBio Rz; positive compression. No smoothing or extrapolation.",
            "nodes":[{"id":int(n),"positionM":vector(p)} for n,p in zip(ids,xyz)],"triangles":triangles,"parts":parts,"frames":frames,"curve":curve,"warnings":warnings}
    data=json.dumps(result,separators=(",",":"),allow_nan=False).encode()
    if len(data)>50_000_000: raise ValueError("Export exceeds Studio's 50-MB viewing budget; reduce --frames")
    if sha(folder/"case.xplt")!=plot_sha or sha(folder/"case.feb")!=input_sha or sha(reaction_path)!=reaction_sha or sha(folder/"process.json")!=process_sha:
        raise ValueError("Source changed during diagnostic export")
    output=output or folder/"diagnostic-result.json";output.parent.mkdir(parents=True,exist_ok=True)
    output.write_bytes(data)
    output.with_suffix(".exporter.py").write_bytes(Path(__file__).read_bytes())
    output.with_suffix(".reader.py").write_bytes((ROOT/"tools/fea-plot-diagnostic.py").read_bytes())
    manifest={"sourceFolder":str(folder),"exporterSha256":sha(Path(__file__)),"readerSha256":sha(ROOT/"tools/fea-plot-diagnostic.py"),
              "reactionFileSha256":reaction_sha,"processFileSha256":process_sha,"diagnosticFileSha256":hashlib.sha256(data).hexdigest(),
              "maximumFixtureErrorM":max(errors),"actualInitialFrame":frames[0]["normalizedTime"]==0,"completeFramesRead":info["completeFramesRead"],"exportedFrames":len(frames),"complete":complete,"curveAccepted":accepted,
              "timeIdentity":info["timeIdentity"],"roundedPlotTimeDuplicates":info["roundedPlotTimeDuplicates"],"maximumPlotTimeRepresentationError":info["maximumPlotTimeRepresentationError"]}
    output.with_suffix(".manifest.json").write_text(json.dumps(manifest,indent=2))
    print(json.dumps({"output":str(output),"nodes":len(ids),"triangles":len(triangles),"bytes":len(data),**manifest}))
    return result


if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--folder",type=Path,required=True);parser.add_argument("--output",type=Path);parser.add_argument("--frames",type=int,default=61)
    args=parser.parse_args()
    if not 1<=args.frames<=61: parser.error("--frames must be in 1..61")
    export(args.folder.resolve(),args.output.resolve() if args.output else None,args.frames)
