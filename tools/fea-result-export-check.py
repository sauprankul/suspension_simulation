"""Independent analytic and corruption checks against retained native coupon data."""
from __future__ import annotations

import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import struct
import tempfile
import xml.etree.ElementTree as ET

import numpy as np

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("exporter",ROOT/"tools/fea-result-export.py")
exporter=importlib.util.module_from_spec(spec);spec.loader.exec_module(exporter)
SOURCE=ROOT/"artifacts/febio-verification/coupon-0_0060-0.30-896b952b8506"


def run():
    errors=[];rejected=[]
    with tempfile.TemporaryDirectory(prefix="fea-export-check-") as temporary:
        work=Path(temporary);coupon=work/"coupon";shutil.copytree(SOURCE,coupon)
        with contextlib.redirect_stdout(io.StringIO()): result=exporter.export(coupon,work/"result.json")
        xyz=np.array([[n["positionM"][k] for k in ("x","y","z")] for n in result["nodes"]])
        # Independent homogeneous equilibrium from the NH energy, not solver data.
        # W=mu/2*(J^(-2/3) I1-3)+K/2*(J-1)^2; traction-free X/Y.
        for frame in result["frames"]:
            axial=1-frame["compressionM"]/.02;lo,hi=.5,2.
            for _ in range(100):
                lateral=(lo+hi)/2;j=lateral*lateral*axial;i1=2*lateral*lateral+axial*axial
                px=1e6*j**(-2/3)*(lateral-i1/(3*lateral))+1e8*(j-1)*j/lateral
                if px>0: hi=lateral
                else: lo=lateral
            lateral=(lo+hi)/2;j=lateral*lateral*axial;i1=2*lateral*lateral+axial*axial
            pz=1e6*j**(-2/3)*(axial-i1/(3*axial))+1e8*(j-1)*j/axial
            expected_force=-pz*.02**2
            u=np.array([[p[k] for k in ("x","y","z")] for p in frame["displacementsM"]])
            error=float(np.max(np.abs(xyz+u-xyz*np.array([lateral,lateral,axial]))));errors.append(error)
            if error>2e-8 or abs(frame["forceN"]-expected_force)>max(1e-4,abs(expected_force)*1e-5):
                raise ValueError("Exported actual force/deformation fails independent analytic coupon")
        assert result["frames"][0]["normalizedTime"]==0 and result["curve"][0]=={"compressionM":0.,"forceN":0.}
        assert result["complete"] and not result["curveAccepted"]  # Legacy verification is not promoted.

        def reject(label,mutation):
            folder=work/label;shutil.copytree(coupon,folder);mutation(folder)
            try:
                with contextlib.redirect_stdout(io.StringIO()): exporter.export(folder,work/(label+".json"))
            except ValueError: rejected.append(label)
            else: raise AssertionError("Malformed source accepted: "+label)

        def bad_input(folder):
            path=folder/"case.feb";path.write_bytes(path.read_bytes().replace(b"0.02,0,0",b"0.03,0,0",1))
        reject("tampered-input",bad_input)

        def active(folder):
            path=folder/"process.json";state=json.loads(path.read_text());state["state"]="running";path.write_text(json.dumps(state))
        reject("active-writer",active)

        def missing_reaction(folder):
            path=folder/"TOP.txt";lines=path.read_text().splitlines();index=next(i for i,line in enumerate(lines) if line.startswith("5 "));del lines[index];path.write_text("\n".join(lines))
        reject("missing-reaction-node",missing_reaction)

        def changed_loading(folder):
            path=folder/"case.feb";root=ET.fromstring(path.read_bytes());root.find("LoadData/load_controller/points/pt").text="0,0.1"
            path.write_bytes(ET.tostring(root));state_path=folder/"process.json";state=json.loads(state_path.read_text());digest=hashlib.sha256(path.read_bytes()).hexdigest();state.update(inputFileSha256=digest,modelSha256=digest);state_path.write_text(json.dumps(state))
        reject("unsupported-load-history",changed_loading)

        def repeated_state(folder):
            path=folder/"case.xplt";data=path.read_bytes();at=4;last=None
            while at+8<=len(data):
                tag,size=struct.unpack_from("<II",data,at);end=at+8+size
                if tag==0x02000000: last=data[at:end]
                at=end
            if not last: raise AssertionError("No plot state")
            path.write_bytes(data+last)
        reject("duplicate-plot-time",repeated_state)
    print(json.dumps({"status":"passed","actualFramesChecked":len(errors),"maximumIndependentPositionErrorM":max(errors),"rejected":rejected}))


if __name__=="__main__": run()
