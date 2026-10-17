"""Synthetic material-identification verification; never a manufacturer material preset.

Requires the local Python/SciPy toolchain. FEA verification uses the pinned bounded CCX adapter.
One uniaxial curve alone does not identify compressibility; lateral observations are included.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import sys

os.environ["OPENBLAS_NUM_THREADS"]="1"
os.environ["MKL_NUM_THREADS"]="1"
import scipy
from scipy.optimize import least_squares

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/"artifacts/fea-foam-fit-spike"


def response(mu,alpha,nu,compression):
    stretch=1-compression
    log=math.log(stretch)
    nominal=-2*mu/stretch*((math.exp(alpha*log)-math.exp(-alpha*nu*log))/alpha if abs(alpha)>1e-7 else (1+nu)*log)
    return nominal,stretch**(-nu)


def fit():
    path=ROOT/"data/fea/synthetic-foam-coupon.json"
    source=json.loads(path.read_text())
    samples=source["samples"]
    stress_scale=max(s["nominalCompressionStressPa"] for s in samples)
    def residual(x):
        mu,alpha,nu=x[0]*1e6,x[1],x[2]
        values=[]
        for s in samples:
            stress,lateral=response(mu,alpha,nu,s["compressionFraction"])
            values.extend([(stress-s["nominalCompressionStressPa"])/stress_scale/(.1+s["compressionFraction"]),
                           (lateral-s["lateralStretch"])/.05])
        return values
    solution=least_squares(residual,[.15,-2.5,.1],bounds=([.001,-20,0],[5.,20,.49]),xtol=1e-12,ftol=1e-12,gtol=1e-12)
    if not solution.success:
        raise ValueError("Material fit did not converge")
    mu,alpha,nu=solution.x[0]*1e6,solution.x[1],solution.x[2]
    truth=source["truthForVerificationOnly"]
    holdout=[]
    for c in [.075,.15,.25,.35,.475]:
        expected=response(truth["muPa"],truth["alpha"],truth["poisson"],c)[0]
        predicted=response(mu,alpha,nu,c)[0]
        holdout.append({"compressionFraction":c,"expectedStressPa":expected,"predictedStressPa":predicted,"relativeError":abs(predicted-expected)/expected})
    if max(h["relativeError"] for h in holdout)>.02 or abs(nu-truth["poisson"])>.005:
        raise ValueError("Synthetic holdout/compressibility recovery gate failed")
    result={"status":"Synthetic coupon identification only; no measured calibration or correlation", "provenance":"synthetic", "law":"CalculiX hyperfoam N=1", "muPa":mu,"alpha":alpha,"poisson":nu,
            "validCompressionFraction":[0,source["maximumCompressionFraction"]],"rawCouponSha256":hashlib.sha256(path.read_bytes()).hexdigest(),"conditions":source["conditions"],
            "nominalStressConvention":"positive compression force/original area; strain=shortening/original height", "lateralDataIncluded":True,"holdouts":holdout,
            "pythonVersion":sys.version.split()[0],"scipyVersion":scipy.__version__,"residualSumSquares":float(sum(v*v for v in solution.fun))}
    OUT.mkdir(parents=True,exist_ok=True)
    (OUT/"fit.json").write_text(json.dumps(result,indent=2))
    return result


def verify(result):
    spec=importlib.util.spec_from_file_location("contact_spike",ROOT/"tools/fea-contact-spike.py")
    module=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    # This pinned Windows build crashes for N=1. N=2 with an exactly zero second shear term
    # is the identical energy law; the independent force test below verifies the encoding.
    material=f"*HYPERFOAM,N=2\n{result['muPa']:.12g},{result['alpha']:.12g},0.,2.,{result['poisson']:.12g},{result['poisson']:.12g}"
    deck=module.cube_contact(1e12,.025,.4,material)
    folder=OUT/"contact-coupon"
    report=module.invoke(folder,deck)
    top=module.totals(folder/"case.dat","PLATEN")
    bottom=module.totals(folder/"case.dat","BOTTOM")
    if not top or abs(top[-1][0]-1)>1e-8 or not bottom:
        raise ValueError("Fitted hyperfoam solve did not complete")
    expected=response(result["muPa"],result["alpha"],result["poisson"],.4)[0]*.02**2
    actual=-top[-1][1][2]
    imbalance=abs(top[-1][1][2]+bottom[-1][1][2])
    if abs(actual-expected)/expected>.01 or imbalance/expected>.01:
        raise ValueError("Fitted foam contact coupon failed independent constitutive/balance gate")
    report.update(expectedForceN=expected,actualForceN=actual,relativeAnalyticError=abs(actual-expected)/expected,imbalanceN=imbalance,
                  backendEncoding="N=2 with zero second shear term; exact one-term energy; avoids pinned Windows N=1 crash")
    result["feaVerification"]=report
    (OUT/"verification.json").write_text(json.dumps(result,indent=2))
    return result


if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--fit-only",action="store_true")
    args=parser.parse_args()
    result=fit()
    if not args.fit_only:
        result=verify(result)
    print(json.dumps({k:result[k] for k in ("status","muPa","alpha","poisson")}))
