"""Read-only deformation diagnostics for the pinned, uncompressed FEBio v0x35 plot format.

This is a narrow benchmark reader, not general STEP/Studio support. It checks plot
node IDs/reference positions against the archived input and never publishes a curve.
Tag definitions: FEBio v4.13 FEBioPlot/FEBioPlotFile.h and writer implementation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET

import numpy as np

ROOT=Path(__file__).resolve().parents[1]


def blocks(data):
    offset=0
    while offset<len(data):
        if offset+8>len(data): raise ValueError("Truncated child block")
        tag,size=struct.unpack_from("<II",data,offset)
        offset+=8
        if offset+size>len(data): raise ValueError("Child block exceeds parent")
        yield tag,data[offset:offset+size]
        offset+=size


def one(data,tag):
    values=[value for key,value in blocks(data) if key==tag]
    if len(values)!=1: raise ValueError(f"Expected one {tag:#x} block")
    return values[0]


def uint(data):
    if len(data)!=4: raise ValueError("Expected one 32-bit integer")
    return struct.unpack("<I",data)[0]


def read_plot(folder,maximum_time=1.,on_frame=None):
    model_bytes=(folder/"case.feb").read_bytes()
    journal=json.loads((folder/"process.json").read_text())
    raw_sha=hashlib.sha256(model_bytes).hexdigest()
    canonical_sha=hashlib.sha256(model_bytes.replace(b"\r\n",b"\n")).hexdigest()
    expected_input_sha=journal.get("inputFileSha256")
    # Earlier Windows research decks recorded the LF model string before
    # write_text translated it to CRLF. Accept only that exact legacy convention.
    if (expected_input_sha is not None and raw_sha!=expected_input_sha) or (expected_input_sha is None and canonical_sha!=journal["modelSha256"]):
        raise ValueError("Input deck differs from the process manifest")
    model=ET.fromstring(model_bytes)
    input_nodes={int(n.attrib["id"]):tuple(map(float,n.text.split(","))) for n in model.findall("Mesh/Nodes/node")}
    plot=folder/"case.xplt";limit=plot.stat().st_size
    ids=coordinates=None;variable=None;latest=None;count=0;version=None;mesh_seen=False
    with plot.open("rb") as stream:
        if uint(stream.read(4))!=0x00464542: raise ValueError("Unsupported plot magic/byte order")
        while stream.tell()+8<=limit:
            tag,size=struct.unpack("<II",stream.read(8))
            if stream.tell()+size>limit: break  # A live writer may have an incomplete final block.
            if size>128*1024**2: raise ValueError("Diagnostic block exceeds bounded reader budget")
            data=stream.read(size)
            if tag==0x01000000:
                header=one(data,0x01010000);version=uint(one(header,0x01010001))
                if version!=0x35 or uint(one(header,0x01010004))!=0:
                    raise ValueError("Only pinned v0x35 uncompressed plots are supported")
                dictionary=one(one(data,0x01020000),0x01023000)
                for index,(kind,item) in enumerate(blocks(dictionary),1):
                    if kind!=0x01020001: raise ValueError("Unexpected dictionary item")
                    name=one(item,0x01020004).split(b"\0",1)[0].decode("ascii")
                    if name=="displacement":
                        if uint(one(item,0x01020002))!=1 or uint(one(item,0x01020003))!=0:
                            raise ValueError("Displacement is not a nodal 3-vector")
                        variable=index
            elif tag==0x01040000:
                if mesh_seen: raise ValueError("Adaptive/replaced meshes are outside this diagnostic")
                mesh_seen=True;node_section=one(data,0x01041000)
                records=one(node_section,0x01041200)
                dtype=np.dtype([("id","<i4"),("xyz","<f4",(3,))])
                if len(records)%dtype.itemsize: raise ValueError("Bad node record width")
                nodes=np.frombuffer(records,dtype=dtype);ids=nodes["id"].astype(int);coordinates=nodes["xyz"].astype(float)
                if len(set(ids))!=len(ids) or set(ids)!=set(input_nodes): raise ValueError("Plot/input node identities disagree")
                expected_positions=np.array([input_nodes[int(n)] for n in ids])
                if not np.all(np.isfinite(coordinates)) or np.max(np.abs(coordinates-expected_positions))>1e-8:
                    raise ValueError("Plot/input reference coordinates or units disagree")
            elif tag==0x02000000:
                if ids is None or variable is None: raise ValueError("State precedes checked mesh/dictionary")
                time_value=struct.unpack("<f",one(one(data,0x02010000),0x02010002))[0]
                if time_value>maximum_time+1e-7: continue
                nodal=one(one(data,0x02020000),0x02020300)
                found=False
                for kind,item in blocks(nodal):
                    if kind!=0x02020001: raise ValueError("Unexpected state variable")
                    if uint(one(item,0x02020002))!=variable: continue
                    if found: raise ValueError("Duplicate displacement state")
                    found=True
                    region=one(one(item,0x02020003),0)
                    if len(region)!=len(ids)*12: raise ValueError("Displacement count differs from mesh")
                    displacement=np.frombuffer(region,dtype="<f4").reshape((-1,3)).astype(float)
                    if not np.all(np.isfinite(displacement)): raise ValueError("Non-finite displacement")
                    if not np.isfinite(time_value) or time_value<0 or (latest is not None and time_value<=latest[0]):
                        raise ValueError("Displacement states are not finite and strictly ordered. XPLT stores float32 times; very small solver increments can round to the same time. Inspect an earlier prefix with --maximum-time; do not merge or invent states.")
                    latest=(time_value,displacement);count+=1
                    if on_frame is not None: on_frame(count-1,time_value,displacement)
                if not found: raise ValueError("Complete plot state lacks displacement")
    if latest is None: raise ValueError("No complete checked displacement state")
    return model,ids,coordinates,latest[1],{"plotVersion":version,"completeFramesRead":count,"normalizedTime":latest[0],"inputFileSha256":raw_sha,"legacyWindowsLineEndings":expected_input_sha is None and raw_sha!=canonical_sha}


def check_fixture(model,ids,displacement,time_value):
    lookup={int(n):i for i,n in enumerate(ids)};prescribed={}
    for bc in model.findall("Boundary/bc"):
        if bc.attrib.get("type")!="prescribed displacement": continue
        axis="xyz".index(bc.findtext("dof"));value=float(bc.findtext("value"))*time_value
        name=bc.attrib["node_set"];text=model.find(f"Mesh/NodeSet[@name='{name}']").text
        for n in map(int,text.split(",")): prescribed[n,axis]=value
    error=max(abs(displacement[lookup[n],axis]-value) for (n,axis),value in prescribed.items())
    if error>2e-8: raise ValueError("Decoded fixture motion disagrees with prescribed displacement")
    return error


def self_check():
    folder=ROOT/"artifacts/febio-verification/coupon-0_0060-0.30-896b952b8506"
    model,ids,xyz,u,info=read_plot(folder)
    error=check_fixture(model,ids,u,info["normalizedTime"])
    # Independent traction-free lateral stretch of the same compressible NH energy.
    a=.7;lo,hi=.5,2.
    for _ in range(100):
        lateral=(lo+hi)/2;j=lateral*lateral*a;i1=2*lateral*lateral+a*a
        derivative=1e6*j**(-2/3)*(lateral-i1/(3*lateral))+1e8*(j-1)*j/lateral
        if derivative>0: hi=lateral
        else: lo=lateral
    expected=xyz*np.array([(lo+hi)/2,(lo+hi)/2,a])
    coupon_error=float(np.max(np.abs(xyz+u-expected)))
    if abs(info["normalizedTime"]-1)>1e-8 or coupon_error>2e-8:
        raise ValueError("Independent homogeneous deformation check failed")
    print(json.dumps({"status":"Pinned plot reader passes independent homogeneous deformation and prescribed-fixture checks","maximumPositionErrorM":coupon_error,"maximumFixtureErrorM":error}))


def draw(folder,maximum_time=None):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.ticker import MaxNLocator
    from mpl_toolkits.mplot3d.art3d import Poly3DCollection
    state=json.loads((folder/"process.json").read_text())
    source_time=state.get("lastConvergedNormalizedTime",1)
    maximum_time=source_time if maximum_time is None else min(maximum_time,source_time)
    model,ids,xyz,u,info=read_plot(folder,maximum_time)
    prefix=info["normalizedTime"]<source_time-1e-7
    info.update(maximumFixtureErrorM=check_fixture(model,ids,u,info["normalizedTime"]),curveAccepted=state.get("curveAccepted",False) and not prefix,sourceModelSha256=state["modelSha256"],sourceLastAcceptedTime=source_time,earlierPrefix=prefix,requestedMaximumTime=maximum_time)
    lookup={int(n):i for i,n in enumerate(ids)}
    patterns={"tet10":[(0,1,2,4,5,6),(0,3,1,7,8,4),(1,3,2,8,9,5),(2,3,0,9,7,6)],
              "hex8":[(0,1,2,3),(4,5,6,7),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],
              "penta6":[(0,1,2),(3,4,5),(0,3,4,1),(1,4,5,2),(2,5,3,0)]}
    groups=[]
    for group in model.findall("Mesh/Elements"):
        kind=group.attrib["type"];faces={}
        for element in group:
            nodes=list(map(int,element.text.split(",")))
            for pattern in patterns[kind]:
                face=[nodes[i] for i in pattern];key=tuple(sorted(face[:3] if kind=="tet10" else face));faces.setdefault(key,[]).append(face)
        triangles=[]
        for records in faces.values():
            if len(records)!=1: continue
            face=records[0]
            order=[(0,3,5),(3,1,4),(5,4,2),(3,4,5)] if len(face)==6 else [(0,1,2),(0,2,3)] if len(face)==4 else [(0,1,2)]
            triangles.extend([[lookup[face[i]] for i in triangle] for triangle in order])
        groups.append((group.attrib["name"],triangles))
    fig=plt.figure(figsize=(12,5.5));axes=[fig.add_subplot(121,projection="3d"),fig.add_subplot(122,projection="3d")]
    all_points=np.vstack([xyz,xyz+u])*1000;center=(all_points.max(axis=0)+all_points.min(axis=0))/2;radius=max(np.ptp(all_points,axis=0))/2*1.08
    for ax,points,title in zip(axes,[xyz,xyz+u],["Initial geometry",f"Converged increment at {info['normalizedTime']*100:.1f}% of requested stroke"]):
        for name,triangles in groups:
            foam=name=="FOAM";vertices=(points[np.array(triangles)]*1000).tolist()
            ax.add_collection3d(Poly3DCollection(vertices,facecolor="#e8a23a" if foam else "#91a7b5",edgecolor="#68451e" if foam else "#61707a",linewidth=.12,alpha=.95 if foam else .25))
        ax.set(xlim=(center[0]-radius,center[0]+radius),ylim=(center[1]-radius,center[1]+radius),zlim=(center[2]-radius,center[2]+radius),xlabel="X (mm)",ylabel="Y (mm)",zlabel="Z (mm)",title=title)
        ax.set_box_aspect((1,1,1));ax.set_proj_type("ortho");ax.view_init(elev=13,azim=-70)
        for axis in (ax.xaxis,ax.yaxis,ax.zaxis): axis.set_major_locator(MaxNLocator(3,prune="both"))
    fig.suptitle("Synthetic bumper deformation — diagnostic only"+(" — earlier prefix" if prefix else "" )+("" if state.get("curveAccepted",False) else " — solve incomplete"),fontsize=13)
    fig.text(.5,.025,"Displacement scale 1×. Quadratic faces tessellated for display. No physical-material correlation claimed.",ha="center",fontsize=9)
    stem="deformation-diagnostic"+(f"-prefix-{maximum_time:.8f}" if prefix else "")
    fig.tight_layout(rect=(0,.04,1,.95));output=folder/(stem+".png");fig.savefig(output,dpi=150);plt.close(fig)
    (folder/(stem+".json")).write_text(json.dumps(info,indent=2));print(json.dumps(info|{"image":str(output)}))


if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--self-check",action="store_true");parser.add_argument("--folder",type=Path)
    parser.add_argument("--maximum-time",type=float,help="Inspect only an earlier unambiguous normalized-time prefix; never repairs or accepts a failed curve.")
    args=parser.parse_args()
    if args.maximum_time is not None and (not np.isfinite(args.maximum_time) or not 0<=args.maximum_time<=1): parser.error("--maximum-time must be finite and between zero and one")
    if args.self_check: self_check()
    elif args.folder: draw(args.folder.resolve(),args.maximum_time)
    else: parser.error("Choose --self-check or --folder")
