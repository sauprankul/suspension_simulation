"""Independent deformation and corruption checks for the tet10 history gate.

Uses retained native data; never invokes a solver or alters the original runs.
"""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import shutil
import struct
import tempfile

import numpy as np

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("geometry",ROOT/"tools/fea-mesh-diagnostic.py")
geometry=importlib.util.module_from_spec(spec);spec.loader.exec_module(geometry)
SOURCE=ROOT/"artifacts/febio-verification/step-0_0060-0.30-55e043035d71"
INCOMPLETE=ROOT/"artifacts/febio-verification/step-0_0060-0.60-24204a4f1561/run-20261018T021330Z-3f1e4ad9"


def children(data,start,end):
    while start<end:
        tag,size=struct.unpack_from("<II",data,start)
        yield tag,start+8,start+8+size
        start+=8+size
    if start!=end: raise AssertionError("Invalid test fixture block extent")


def child(data,start,end,tag):
    found=[(a,b) for key,a,b in children(data,start,end) if key==tag]
    if len(found)!=1: raise AssertionError("Unexpected test fixture layout")
    return found[0]


def run():
    geometry.self_check()
    good=geometry.check_history(SOURCE)
    assert good["framesChecked"]>2 and good["minimumSampledRelativeJacobian"]>0
    rejected=[]
    def reject(label,folder,reason):
        try: geometry.check_history(folder)
        except ValueError as error:
            if reason not in str(error): raise AssertionError(f"Wrong rejection: {error}") from error
            rejected.append(label)
        else: raise AssertionError("Accepted invalid geometry history: "+label)
    # The real failed run contains rounded duplicate float32 plot times. Never
    # invent chronology or silently omit states to make a verification pass.
    reject("ambiguous-native-times",INCOMPLETE,"strictly ordered")
    with tempfile.TemporaryDirectory(prefix="fea-geometry-history-") as temporary:
        folder=Path(temporary)
        for name in ("case.feb","case.xplt","process.json"):
            shutil.copyfile(SOURCE/name,folder/name)
        data=bytearray((folder/"case.xplt").read_bytes())
        states=[(a,b) for key,a,b in children(data,4,len(data)) if key==0x02000000]
        # In this native fixture, displacement is dictionary variable 1.
        a,b=child(data,*states[-1],0x02020000)
        a,b=child(data,a,b,0x02020300)
        variables=[(a,b) for key,a,b in children(data,a,b) if key==0x02020001]
        chosen=[]
        for a,b in variables:
            i,j=child(data,a,b,0x02020002)
            if struct.unpack_from("<I",data,i)[0]==1: chosen.append((a,b))
        assert len(chosen)==1
        a,b=child(data,*chosen[0],0x02020003)
        a,b=child(data,a,b,0)
        values=np.frombuffer(data,dtype="<f4",count=(b-a)//4,offset=a)
        assert np.max(np.abs(values))>1e-4
        # Catastrophic over-compression reverses tetrahedra without changing
        # times, node identities, the reference mesh or input manifest.
        values*=1e6
        del values
        (folder/"case.xplt").write_bytes(data)
        reject("inverted-native-state",folder,"Nonpositive sampled deformed Jacobian")
        shutil.copyfile(SOURCE/"case.xplt",folder/"case.xplt")
        # Removing the final actual state must not count as a complete solve.
        data=(folder/"case.xplt").read_bytes()
        (folder/"case.xplt").write_bytes(data[:states[-1][0]-8])
        reject("missing-final-state",folder,"complete requested stroke")
    print(json.dumps({"status":"passed","actualFramesChecked":good["framesChecked"],
                      "minimumSampledRelativeJacobian":good["minimumSampledRelativeJacobian"],"rejected":rejected}))


if __name__=="__main__": run()
