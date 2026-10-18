"""Sequential, restartable feasibility study; never publishes a production curve.

An existing guarded full-Newton fold attempt can be adopted. Each subsequent
worker keeps its own immutable inputs/results. Any failed gate stops the study.
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
import sys
import time
from types import SimpleNamespace

import psutil

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts/febio-verification"
STUDY = OUT / "feasibility-study.json"
_io_spec=importlib.util.spec_from_file_location("study_journal_io",ROOT/"tools/fea-contact-spike.py")
_io=importlib.util.module_from_spec(_io_spec);_io_spec.loader.exec_module(_io)


def write(state):
    state["updatedAtUtc"] = datetime.now(timezone.utc).isoformat()
    _io.write_json_atomic(STUDY,state)


def verified(folder):
    status = json.loads((folder/"process.json").read_text())
    report = json.loads((folder/"verification.json").read_text())
    raw = hashlib.sha256((folder/"case.feb").read_bytes()).hexdigest()
    if status["state"] != "verified" or not status.get("curveAccepted") or not report.get("curveAccepted") or raw != status["inputFileSha256"] or report["modelSha256"] != raw:
        raise ValueError("Missing or mismatched complete case verification: "+str(folder))
    if (folder/"request.json").exists():
        # An adopted process may have started with an older verifier snapshot.
        # Recheck its unchanged raw histories with the study's current gates.
        spec=importlib.util.spec_from_file_location("study_case_verifier",ROOT/"tools/fea-febio-spike.py")
        verifier=importlib.util.module_from_spec(spec);spec.loader.exec_module(verifier)
        request=json.loads((folder/"request.json").read_text())
        import xml.etree.ElementTree as ET
        model=ET.fromstring((folder/"case.feb").read_bytes())
        sets={item.attrib["name"]:list(map(int,item.text.split(","))) for item in model.findall("Mesh/NodeSet")}
        checked=verifier.verify_results(folder,{},request["metadata"],SimpleNamespace(**request["arguments"]),sets)
        if report["history"]!=checked["history"] or report["forceN"]!=checked["forceN"]:
            raise ValueError("Archived reaction verification no longer matches native histories")
        recheck={"verifierSourceSha256":hashlib.sha256((ROOT/"tools/fea-febio-spike.py").read_bytes()).hexdigest(),
                 "inputFileSha256":raw,"forceN":checked["forceN"],"imbalanceN":checked["imbalanceN"],"completeReactionAndContactHistories":True}
        (folder/("study-recheck-"+recheck["verifierSourceSha256"][:12]+".json")).write_text(json.dumps(recheck,indent=2))
    return report


def active_workers():
    return [p.pid for p in psutil.process_iter(["name"]) if p.info["name"] and p.info["name"].lower()=="febio4.exe"]


def adopt(folder,state):
    request = json.loads((folder/"request.json").read_text())["arguments"]
    expected = {"case":"step","compression":.6,"mesh":"0_0060","fold_contact":True,"guide_shaft":True}
    if any(request.get(key)!=value for key,value in expected.items()):
        raise ValueError("Adoption requires the declared 60% grooved STEP/shaft baseline; numerical settings remain pinned in its request")
    state.update(state="waiting_for_baseline",activeFolder=str(folder));write(state)
    while True:
        journal=json.loads((folder/"process.json").read_text())
        if journal["state"]=="verified": return verified(folder)
        if journal["state"] in ("failed","canceled","validation_failed"):
            raise ValueError("Baseline did not pass: "+journal.get("error",journal["state"]))
        if journal.get("seconds",0)>journal["timeoutSeconds"]+30:
            raise ValueError("Baseline process journal exceeded its declared budget")
        if journal["state"]=="running":
            try:
                process=psutil.Process(journal["pid"])
                if str(folder/"case.feb").lower() not in [arg.lower() for arg in process.cmdline()]:
                    raise ValueError("Baseline PID no longer identifies this job")
            except psutil.NoSuchProcess:
                # The wrapper can be completing its terminal journal/postchecks.
                time.sleep(1)
                newer=json.loads((folder/"process.json").read_text())
                if newer["state"]=="running": raise ValueError("Baseline worker disappeared with a stale running journal")
        state["baselineElapsedSeconds"]=journal.get("seconds")
        state["baselineLastAcceptedFraction"]=journal.get("lastConvergedNormalizedTime")
        write(state);time.sleep(5)


def run(label,arguments,state):
    if active_workers(): raise ValueError("Another FEBio worker is active; do not overlap heavy cases")
    state.update(state="running_case",activeCase=label);write(state)
    log=OUT/("study-"+label+".log")
    # Logs are attempt-specific; never replace an earlier command transcript.
    log=log.with_name(log.stem+"-"+datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")+".log")
    with log.open("w") as stream:
        process=subprocess.Popen([sys.executable,*arguments],cwd=ROOT,stdout=stream,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
        state.update(wrapperPid=process.pid,commandArguments=arguments,commandLog=str(log));write(state)
        while process.poll() is None:
            write(state);time.sleep(2)
    text=log.read_text()
    if process.returncode: raise ValueError(label+" failed; see "+str(log))
    records=[]
    for line in text.splitlines():
        try: records.append(json.loads(line))
        except json.JSONDecodeError: pass
    folders=[record["folder"] for record in records if "folder" in record]
    if not folders: raise ValueError("Worker did not report its immutable result folder")
    folder=Path(folders[-1]);report=verified(folder)
    state["cases"][label]=str(folder);write(state)
    return report


def curve(report):
    points=[]
    for row in report["history"]:
        t=row["time"];force=sum(n[3] for n in row["values"])
        if not math.isfinite(force) or not math.isfinite(t) or t<0 or (points and t<=points[-1][0]): raise ValueError("Case force samples are not finite/ordered")
        points.append((t,force))
    if len(points)<2 or points[0][0]!=0 or abs(points[0][1])>1e-5 or abs(points[-1][0]-1)>1e-8:
        raise ValueError("Case lacks its zero-reaction contact origin or complete requested stroke")
    return points


def interpolate(points,x):
    import bisect
    i=min(max(1,bisect.bisect_left([p[0] for p in points],x)),len(points)-1)
    a,b=points[i-1],points[i]
    return a[1]+(b[1]-a[1])*(x-a[0])/(b[0]-a[0])


def compare(a,b):
    first,second=curve(a),curve(b)
    times=sorted(set([p[0] for p in first]+[p[0] for p in second]))
    scale=max(abs(p[1]) for p in second)
    error=max(abs(interpolate(first,t)-interpolate(second,t)) for t in times)
    result={"maximumDifferenceN":error,"declaredForceScaleN":scale,"relativeDifference":error/scale,"threshold":.05,
            "basis":"Union of accepted sample times; piecewise-linear curves; contact-origin zero"}
    if error>max(1e-5,.05*scale): raise ValueError("Curve sensitivity exceeds 5%: "+json.dumps(result))
    return result


def material_evidence(path,state):
    """Reuse completed, rechecked cases; never rerun a passing unchanged study."""
    evidence=json.loads(path.read_text())
    foam=Path(evidence["foamFolder"]).resolve();foam_report=verified(foam)
    # The foam worker has no generic request.json: independently recheck its
    # exact fitted energy, actual contacts/reactions, and analytic whole history.
    spec=importlib.util.spec_from_file_location("foam_evidence",ROOT/"tools/fea-febio-foam-spike.py")
    helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
    spec=importlib.util.spec_from_file_location("foam_contact_evidence",ROOT/"tools/fea-febio-spike.py")
    verifier=importlib.util.module_from_spec(spec);spec.loader.exec_module(verifier)
    import xml.etree.ElementTree as ET
    model=ET.fromstring((foam/"case.feb").read_bytes());fit=json.loads((foam/"fit.json").read_text())
    material=model.find("Material/material[@id='1']")
    if material.attrib.get("type")!="Ogden unconstrained" or any(float(material.findtext(key))!=fit[name] for key,name in (("cp","cpPa"),("c1","c1Pa"),("m1","m1"))):
        raise ValueError("Fitted foam law differs from its actual input")
    if hashlib.sha256((foam/"source-coupon.json").read_bytes()).hexdigest()!=fit["sourceSha256"]:
        raise ValueError("Foam fitting evidence identity changed")
    sets={item.attrib["name"]:list(map(int,item.text.split(","))) for item in model.findall("Mesh/NodeSet")}
    checked=verifier.verify_results(foam,{}, {},SimpleNamespace(case="foam-contact",fold_contact=False,guide_shaft=False),sets)
    if foam_report["history"]!=checked["history"]: raise ValueError("Foam reaction history differs from verification")
    errors=[]
    for frame in checked["history"]:
        expected=helper.response(fit["cpPa"],fit["c1Pa"],fit["m1"],.4*frame["time"])[0]*.02**2
        if expected>1e-5: errors.append(abs(sum(v[3] for v in frame["values"])-expected)/expected)
    if not errors or max(errors)>.01 or helper.self_check()>1e-7: raise ValueError("Independent foam energy/reaction recheck failed")
    state["cases"]["compressible-foam-coupon"]=str(foam)
    rubber=[]
    for size,item in zip(("0_0060","0_0045","0_0035"),evidence["cases"]):
        case=Path(item["folder"]).resolve();request=json.loads((case/"request.json").read_text())["arguments"]
        if request["case"]!="rubber" or request["compression"]!=.3 or request["mesh"]!=size: raise ValueError("Wrong declared rubber mesh/material case")
        rubber.append(verified(case));state["cases"]["rubber-"+size]=str(case)
    if len(evidence["cases"])!=3: raise ValueError("Exactly three completed rubber meshes are required")
    state["comparisons"]["rubberMediumFine"]=compare(rubber[1],rubber[2])
    reference=ROOT/"artifacts/fea-contact-spike/rubber-axisymmetric/12-24/verification.json"
    expected=json.loads(reference.read_text())["forceN"]
    difference=abs(rubber[-1]["forceN"]-expected)/expected
    if difference>.05: raise ValueError("Rubber/reference force gate failed")
    state.update(materialEvidenceSha256=hashlib.sha256(path.read_bytes()).hexdigest(),rubberReferenceDifference=difference,
                 rubberReferenceSha256=hashlib.sha256(reference.read_bytes()).hexdigest(),foamMaximumRelativeAnalyticError=max(errors))
    write(state)


def main(folder,materials=None):
    state={"state":"starting","controllerPid":os.getpid(),"controllerSourceSha256":hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),"startedAtUtc":datetime.now(timezone.utc).isoformat(),"backendGatePassed":False,
           "productionCurvePublished":False,"cases":{},"comparisons":{},"adoptedBaseline":str(folder)}
    write(state)
    try:
        if materials is not None: material_evidence(materials,state)
        baseline=adopt(folder,state);state["cases"]["fold-coarse"]=str(folder)
        settings=json.loads((folder/"request.json").read_text())["arguments"]
        common=["tools/fea-febio-spike.py","--case","step","--compression",".6","--fold-contact","--guide-shaft",
                "--max-updates",str(settings["max_updates"]),"--max-reformations",str(settings.get("max_reformations",35)),"--opt-iterations",str(settings["opt_iterations"]),
                "--line-search-tolerance",str(settings.get("line_search_tolerance",.9)),"--timeout-hours","8"]
        if settings["guarded_line_search"]: common.append("--guarded-line-search")
        penalty=settings["penalty"];increment=settings["increment"]
        medium=run("fold-medium",common+["--mesh","0_0045","--penalty",str(penalty),"--increment",str(increment)],state)
        fine=run("fold-fine",common+["--mesh","0_0035","--penalty",str(penalty),"--increment",str(increment)],state)
        state["comparisons"]["mediumFine"]=compare(medium,fine);write(state)
        contact=run("fold-contact-sensitivity",common+["--mesh","0_0045","--penalty",str(penalty*10),"--increment",str(increment)],state)
        state["comparisons"]["contact"]=compare(medium,contact);write(state)
        smaller_step=run("fold-increment-sensitivity",common+["--mesh","0_0045","--penalty",str(penalty),"--increment",str(increment/2)],state)
        state["comparisons"]["increment"]=compare(medium,smaller_step);write(state)
        if materials is not None:
            state.update(state="numerical_cases_passed_requires_review",backendGatePassed=False,
                         review="Review all case diagnostics, element formulation and supported recipes before admitting the backend to Studio. Physical correlation remains unmet.")
            write(state);return
        run("compressible-foam-coupon",["tools/fea-febio-foam-spike.py"],state)
        rubber=[]
        for size in ("0_0060","0_0045","0_0035"):
            rubber.append(run("rubber-"+size,["tools/fea-febio-spike.py","--case","rubber","--mesh",size,"--penalty","1e11","--guarded-line-search","--timeout-hours","8"],state))
        reference=ROOT/"artifacts/fea-contact-spike/rubber-axisymmetric/12-24/verification.json"
        expected=json.loads(reference.read_text())["forceN"]
        state["comparisons"]["rubberMediumFine"]=compare(rubber[1],rubber[2])
        difference=abs(rubber[-1]["forceN"]-expected)/expected
        if difference>.05: raise ValueError("Nearly incompressible FEBio/independent CAX8R reference differs by more than 5%")
        state.update(state="numerical_cases_passed_requires_review",rubberReferenceDifference=difference,
                     backendGatePassed=False,review="Review all case diagnostics, element formulation and supported recipes before admitting the backend to Studio. Physical correlation remains unmet.")
        write(state)
    except Exception as error:
        state.update(state="stopped_at_failed_gate",error=str(error),backendGatePassed=False);write(state)
        raise


if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--adopt-baseline",type=Path,required=True)
    parser.add_argument("--material-study",type=Path,help="Reuse exact previously completed foam/three-mesh rubber evidence after rechecking native histories")
    args=parser.parse_args();OUT.mkdir(parents=True,exist_ok=True)
    # Only one orchestrator may own this sequential study at a time.
    import msvcrt
    with (OUT/"feasibility-study.lock").open("a+b") as lock:
        lock.write(b"0");lock.flush();lock.seek(0)
        try: msvcrt.locking(lock.fileno(),msvcrt.LK_NBLCK,1)
        except OSError: raise SystemExit("The feasibility study is already running")
        main(args.adopt_baseline.resolve(),args.material_study.resolve() if args.material_study else None)
