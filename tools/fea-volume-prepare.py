"""Prepare a quadratic volume mesh from an exact saved Studio recipe/inspection.

This is geometry preparation, not a solver or backend-admission bypass. Original
CAD and face identities are retained; initial sampled element quality is checked.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import subprocess
import time
import uuid

import psutil

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("ccx_mesh_reference",ROOT/"tools/fea-contact-spike.py")
ccx=importlib.util.module_from_spec(spec);spec.loader.exec_module(ccx)


def vector(value):
    values=tuple(float(value[k]) for k in ("x","y","z"))
    if not all(math.isfinite(v) for v in values): raise ValueError("Non-finite frame/vector")
    return values


def basis(frame):
    origin,z,x=(vector(frame[k]) for k in ("originM","axis","referenceDirection"))
    dot=lambda a,b:sum(v*w for v,w in zip(a,b))
    if abs(dot(x,x)-1)>1e-8 or abs(dot(z,z)-1)>1e-8 or abs(dot(x,z))>1e-8:
        raise ValueError("Compression frame must be orthonormal")
    y=(z[1]*x[2]-z[2]*x[1],z[2]*x[0]-z[0]*x[2],z[0]*x[1]-z[1]*x[0])
    return origin,(x,y,z)


def local(point,frame):
    origin,axes=basis(frame)
    return tuple(sum((point[i]-origin[i])*axis[i] for i in range(3)) for axis in axes)


def bytes_hash(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def bounded_run(folder,label,arguments):
    env=dict(os.environ,OMP_NUM_THREADS="1",OPENBLAS_NUM_THREADS="1",MKL_NUM_THREADS="1")
    start=time.perf_counter();peak=0
    with (folder/(label+".stdout.log")).open("w") as out,(folder/(label+".stderr.log")).open("w") as error:
        process=subprocess.Popen([str(ccx.GMSH),*arguments],cwd=folder,env=env,stdout=out,stderr=error,creationflags=subprocess.CREATE_NO_WINDOW)
        monitor=psutil.Process(process.pid)
        try:
            while process.poll() is None:
                try: peak=max(peak,monitor.memory_info().rss)
                except psutil.NoSuchProcess: pass
                if (folder/"cancel.request").exists(): raise ValueError("Volume preparation canceled")
                if time.perf_counter()-start>300 or peak>2*1024**3: raise TimeoutError("Mesher exceeded its five-minute/2-GB budget")
                time.sleep(.05)
        finally:
            if process.poll() is None: process.kill();process.wait()
    if process.returncode: raise ValueError("Mesher failed; inspect "+str(folder/(label+".stderr.log")))
    return {"seconds":time.perf_counter()-start,"peakWorkingSetBytes":peak}


def parse_inp(path):
    nodes,elements={},{};mode=None
    for line in path.read_text().splitlines():
        if line.startswith("*"):
            upper=line.upper();mode="nodes" if upper.startswith("*NODE") else "tet" if upper.startswith("*ELEMENT") and "TYPE=C3D10" in upper.replace(" ","") else None
        elif line.strip() and mode:
            values=[v.strip() for v in line.split(",")]
            if mode=="nodes": nodes[int(values[0])]=tuple(map(float,values[1:4]))
            else:
                if len(values)!=11: raise ValueError("Expected quadratic tetrahedra from native conversion")
                elements[int(values[0])]=list(map(int,values[1:]))
    used={n for element in elements.values() for n in element}
    if not elements or not used.issubset(nodes): raise ValueError("Volume mesh is empty or has missing nodes")
    return {n:nodes[n] for n in sorted(used)},elements


def cad_faces(path):
    lines=path.read_text().splitlines();at=lines.index("$Elements");count=int(lines[at+1]);faces={}
    for line in lines[at+2:at+2+count]:
        values=list(map(int,line.split()));kind,tag_count=values[1:3]
        if kind!=9: continue  # Gmsh quadratic triangle, tagged with its CAD entity.
        if tag_count<2: raise ValueError("Volume boundary lacks CAD entity mapping")
        tag=values[4];nodes=values[3+tag_count:]
        if len(nodes)!=6: raise ValueError("Invalid boundary triangle width")
        faces.setdefault(tag,[]).append(nodes)
    return faces


def prepare(recipe_path,inspection_path,source_path,size=None):
    recipe=json.loads(recipe_path.read_text());inspection=json.loads(inspection_path.read_text())
    if recipe["inspectionSha256"]!=bytes_hash(inspection_path) or inspection["sourceSha256"]!=bytes_hash(source_path):
        raise ValueError("Saved recipe/inspection/source identities disagree")
    if inspection["targetUnit"]!="m" or inspection["healingApplied"] or inspection["mesherSha256"]!=ccx.GMSH_SHA or bytes_hash(ccx.GMSH)!=ccx.GMSH_SHA:
        raise ValueError("Expected SI, unchanged STEP and pinned mesher")
    if recipe["mesh"]["elementOrder"]!=2: raise ValueError("Production preparation requires quadratic elements")
    target=recipe["mesh"]["targetElementSizeM"] if size is None else size
    if not math.isfinite(target) or not 1e-6<=target<=1: raise ValueError("Invalid explicit mesh size")
    face_tags=[int(face["tag"]) for face in inspection["faces"]]
    if len(face_tags)!=len(set(face_tags)) or min(face_tags)<=0: raise ValueError("CAD face identities must be unique positive tags")
    folder=ROOT/"artifacts/bump-studio-volume"/("run-"+datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")+"-"+uuid.uuid4().hex[:8]);folder.mkdir(parents=True)
    (folder/"input.step").write_bytes(source_path.read_bytes());(folder/"inspection.json").write_bytes(inspection_path.read_bytes());(folder/"recipe.json").write_bytes(recipe_path.read_bytes())
    (folder/"source-driver.py").write_bytes(Path(__file__).read_bytes())
    state={"state":"preparing","folder":str(folder),"recipeSha256":bytes_hash(recipe_path),"inspectionSha256":bytes_hash(inspection_path),
           "sourceSha256":bytes_hash(source_path),"mesherSha256":ccx.GMSH_SHA,"healingApplied":False,"targetElementSizeM":target,"curveAccepted":False}
    def journal():
        temporary=folder/"process.tmp.json";temporary.write_text(json.dumps(state,indent=2));temporary.replace(folder/"process.json")
    journal()
    try:
        geo='''SetFactory("OpenCASCADE");
Geometry.OCCTargetUnit="M";
Geometry.OCCFixDegenerated=0;
Geometry.OCCFixSmallEdges=0;
Geometry.OCCFixSmallFaces=0;
Geometry.OCCSewFaces=0;
Geometry.OCCMakeSolids=0;
Merge "input.step";
v()=Volume{:};
Printf("SOLID_COUNT=%g",#v());
Physical Volume("bumper",1000000)={v()};
Mesh.Binary=0;
Mesh.ElementOrder=2;
Mesh.HighOrderOptimize=2;
'''
        geo+=f"Mesh.MeshSizeMin={target:.12g};\nMesh.MeshSizeMax={target:.12g};\n"
        for tag in face_tags: geo+=f'Physical Surface("cad_{tag}",{tag})={{{tag}}};\n'
        (folder/"volume.geo").write_text(geo)
        state["meshing"]=bounded_run(folder,"volume",["volume.geo","-3","-nt","1","-format","msh2","-o","volume.msh","-v","4","-noenv","-nopopup"])
        log=(folder/"volume.stdout.log").read_text()+(folder/"volume.stderr.log").read_text()
        if "SOLID_COUNT=1\n" not in log or "Error   :" in log: raise ValueError("Expected exactly one unchanged solid and an error-free volume mesh")
        state["conversion"]=bounded_run(folder,"conversion",["volume.msh","-0","-nt","1","-format","inp","-o","volume.inp","-v","4","-noenv","-nopopup"])
        nodes,elements=parse_inp(folder/"volume.inp");faces=cad_faces(folder/"volume.msh")
        if set(faces)!=set(face_tags): raise ValueError("Volume meshing changed the declared CAD-face mapping")
        for i,key in enumerate(("x","y","z")):
            # Surface/volume meshes sample curved extrema differently. This is
            # a unit-scale cross-check, not a claim that nodal extrema are CAD
            # dimensions. The exact source and OCC configuration are also pinned.
            tolerance=max(1e-6,.02*max(target,inspection["previewMeshSizeM"]))
            if abs(min(p[i] for p in nodes.values())-inspection["bounds"]["minimumM"][key])>tolerance or abs(max(p[i] for p in nodes.values())-inspection["bounds"]["maximumM"][key])>tolerance:
                raise ValueError("Volume mesh and inspected STEP dimensions/units disagree")
        converted={n:local(p,recipe["compressionFrameInCad"]) for n,p in nodes.items()}
        quality=ccx.quadratic_quality(converted,elements)
        outer={}
        for element,vertices in elements.items():
            for side,pattern in enumerate(ccx.TET_FACES,1):
                ns=[vertices[i] for i in pattern];key=tuple(sorted(ns[:3]));outer.setdefault(key,[]).append((element,side,ns))
        cad={}
        for tag,triangles in faces.items():
            mapped=[]
            for triangle in triangles:
                matches=outer.get(tuple(sorted(triangle[:3])),[])
                if len(matches)!=1 or set(matches[0][2])!=set(triangle): raise ValueError("CAD face does not match a unique quadratic volume boundary")
                e,side,ns=matches[0];mapped.append({"elementId":e,"side":side,"nodes":ns})
            cad[tag]=mapped
        output={"nodes":converted,"tetrahedra":elements,"cadFaces":cad,"quality":quality,"compressionFrameInCad":recipe["compressionFrameInCad"],
                "units":"m","role":"Prepared volume geometry only; no compression/contact solve or material verification"}
        path=folder/"volume.json";path.write_text(json.dumps(output,separators=(",",":")))
        state.update(state="prepared",volumeSha256=bytes_hash(path),quality=quality,nodes=len(nodes),quadraticTetrahedra=len(elements));journal()
        return state
    except Exception as error:
        state.update(state="failed",error=str(error));journal();raise


if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--recipe",type=Path,required=True);parser.add_argument("--inspection",type=Path,required=True);parser.add_argument("--source",type=Path,required=True);parser.add_argument("--mesh-size",type=float)
    args=parser.parse_args();print(json.dumps(prepare(args.recipe,args.inspection,args.source,args.mesh_size)))
