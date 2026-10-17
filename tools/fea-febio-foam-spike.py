"""Independent FEBio compressible-Ogden identification and contact verification.

This is an explicitly synthetic material, not TPU/Cellasto or a transferred CCX
hyperfoam card. The documented FEBio energy is differentiated independently.
Source: febiosoftware.github.io/febio-docs/features/features/solid_material_ogden_unconstrained/
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
from types import SimpleNamespace
import uuid
import xml.etree.ElementTree as ET

os.environ["OPENBLAS_NUM_THREADS"] = "1"
os.environ["MKL_NUM_THREADS"] = "1"
from scipy.optimize import brentq, least_squares

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "data/fea/synthetic-febio-foam-coupon.json"
OUT = ROOT / "artifacts/febio-foam-verification"


def energy(stretches, cp, c, m):
    j = math.prod(stretches)
    return .5*cp*(j-1)**2 + c/m**2*(sum(s**m for s in stretches)-3-m*math.log(j))


def nominal(stretches, cp, c, m, axis):
    j = math.prod(stretches)
    a = stretches[axis]
    return (cp*j*(j-1) + c/m*(a**m-1))/a


def response(cp, c, m, compression):
    a = 1-compression
    b = brentq(lambda b: nominal((b,b,a),cp,c,m,0),.25,3.,xtol=1e-14)
    return -nominal((b,b,a),cp,c,m,2), b


def self_check():
    maximum = 0.
    for stretches in ((.92,1.03,1.1),(.7,.83,1.21),(1.4,.6,.55)):
        for axis in range(3):
            before=list(stretches);after=list(stretches);h=1e-6
            before[axis]-=h;after[axis]+=h
            numeric=(energy(after,180000.,580000.,-3.)-energy(before,180000.,580000.,-3.))/(2*h)
            analytic=nominal(stretches,180000.,580000.,-3.,axis)
            error=abs(numeric-analytic)/max(1,abs(analytic));maximum=max(maximum,error)
    if maximum>1e-7: raise ValueError("Independent energy derivative check failed")
    return maximum


def fit():
    source=json.loads(SOURCE.read_text())
    samples=source["samples"]
    scale=max(s["nominalCompressionStressPa"] for s in samples)
    def residual(x):
        values=[]
        for row in samples:
            stress,b=response(x[0]*1e6,x[1]*1e6,x[2],row["compressionFraction"])
            values.extend([(stress-row["nominalCompressionStressPa"])/scale/(.1+row["compressionFraction"]),
                           (b-row["lateralStretch"])/.05])
        return values
    solution=least_squares(residual,[.15,.5,-2.5],bounds=([.001,.001,-10],[5,5,-.1]),xtol=1e-12,ftol=1e-12,gtol=1e-12)
    if not solution.success: raise ValueError("Synthetic Ogden fit failed")
    cp,c,m=solution.x[0]*1e6,solution.x[1]*1e6,solution.x[2]
    truth=source["truthForVerificationOnly"]
    holdouts=[]
    for compression in (.075,.15,.25,.35,.475):
        expected,b0=response(truth["cpPa"],truth["c1Pa"],truth["m1"],compression)
        actual,b=response(cp,c,m,compression)
        holdouts.append({"compressionFraction":compression,"relativeStressError":abs(actual-expected)/expected,"lateralError":abs(b-b0)})
    if max(h["relativeStressError"] for h in holdouts)>.01 or max(h["lateralError"] for h in holdouts)>.001:
        raise ValueError("Held-out stress/lateral checks failed")
    previous=-1.
    for compression in (i/200 for i in range(101)):
        stress,b=response(cp,c,m,compression)
        if stress<previous or b*b*(1-compression)<=0 or not math.isfinite(stress):
            raise ValueError("Synthetic fitted response is invalid inside its declared range")
        previous=stress
    return {"status":"Synthetic FEBio-law identification only; no commercial material calibration",
            "law":"Ogden unconstrained", "cpPa":cp,"c1Pa":c,"m1":m,
            "sourceSha256":hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
            "conditions":source["conditions"],"validCompressionFraction":[0,.5],
            "holdouts":holdouts,"energyDerivativeMaximumRelativeError":self_check(),
            "residualSumSquares":float(sum(v*v for v in solution.fun)),
            "note":"A distinct fitted FEBio energy; CCX hyperfoam coefficients are never copied into it. Uniaxial checks do not establish multiaxial stability or physical correlation."}


def verify(result):
    spec=importlib.util.spec_from_file_location("febio_spike",ROOT/"tools/fea-febio-spike.py")
    febio=importlib.util.module_from_spec(spec);spec.loader.exec_module(febio)
    nodes,groups,sets,surfaces=febio.coupon(True)
    root=ET.fromstring(febio.make_model(nodes,groups,sets,surfaces,.4,.02,coupon=True,penalty=1e10,guarded_line_search=True))
    material=root.find("Material/material[@id='1']");material.clear()
    material.attrib.update(id="1",name="SyntheticNH",type="Ogden unconstrained")
    for name,value in (("cp",result["cpPa"]),("c1",result["c1Pa"]),("m1",result["m1"])):
        febio.add(material,name,value)
    ET.indent(root)
    model='<?xml version="1.0" encoding="UTF-8"?>\n'+ET.tostring(root,encoding="unicode")
    folder=OUT/("run-"+datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")+"-"+uuid.uuid4().hex[:8]);folder.mkdir(parents=True)
    (folder/"source-driver.py").write_bytes(Path(__file__).read_bytes())
    (folder/"source-febio-adapter.py").write_bytes((ROOT/"tools/fea-febio-spike.py").read_bytes())
    (folder/"source-coupon.json").write_bytes(SOURCE.read_bytes())
    (folder/"fit.json").write_text(json.dumps(result,indent=2))
    print(json.dumps({"state":"starting","folder":str(folder)}),flush=True)
    report=febio.invoke(folder,model,timeout=240)
    febio.validation_status(folder,"validating")
    try:
        # The generic history/balance/contact checks run without the NH-specific
        # analytic check. Compare the fitted Ogden law independently below.
        report=febio.verify_results(folder,report,{},SimpleNamespace(case="step",fold_contact=False,guide_shaft=False),sets)
        cp,c,m=result["cpPa"],result["c1Pa"],result["m1"]
        errors=[]
        for frame in report["history"]:
            expected=response(cp,c,m,.4*frame["time"])[0]*.02**2
            actual=sum(v[3] for v in frame["values"])
            if expected>1e-5: errors.append(abs(actual-expected)/expected)
        if not errors or max(errors)>.01: raise ValueError("Ogden contact history failed independent analytic force check")
        report.update(material=result,maximumRelativeAnalyticError=max(errors),curveAccepted=True,
                      claim="Numerically verified synthetic compressible Ogden contact coupon only")
        (folder/"verification.json").write_text(json.dumps(report,indent=2))
        febio.validation_status(folder,"verified",curveAccepted=True,verificationPath=str(folder/"verification.json"))
    except Exception as error:
        febio.validation_status(folder,"validation_failed",curveAccepted=False,error=str(error));raise
    print(json.dumps({"folder":str(folder),"forceN":report["forceN"],"maximumRelativeAnalyticError":max(errors)}))


if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--fit-only",action="store_true")
    args=parser.parse_args();result=fit();OUT.mkdir(parents=True,exist_ok=True)
    (OUT/"fit.json").write_text(json.dumps(result,indent=2))
    if args.fit_only: print(json.dumps(result))
    else: verify(result)
