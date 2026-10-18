"""Bounded, read-only summaries of native FEBio iteration/cutback evidence.

No convergence criterion is changed and no force curve is accepted by this tool.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import re


NUMBER=r"[-+\d.eE]+"


def analyze(folder):
    state=json.loads((folder/"process.json").read_text())
    data=(folder/"case.feb").read_bytes()
    raw=hashlib.sha256(data).hexdigest()
    if "inputFileSha256" in state:
        if raw!=state["inputFileSha256"]: raise ValueError("Input differs from process manifest")
    elif hashlib.sha256(data.replace(b"\r\n",b"\n")).hexdigest()!=state["modelSha256"]:
        raise ValueError("Legacy canonical input differs from process manifest")
    path=folder/"case.log";size=path.stat().st_size;limit=16*1024**2
    with path.open("rb") as stream:
        stream.seek(max(0,size-limit));log=stream.read(limit).decode(errors="replace")
    attempts=[]
    for text in re.split(r"===== beginning time step ",log)[1:]:
        heading=re.match(rf"(\d+)\s*:\s*({NUMBER})",text)
        norms={}
        for key in ("residual","energy","displacement"):
            found=re.findall(rf"\s+{key}\s+({NUMBER})\s+({NUMBER})\s+({NUMBER})",text)
            if found: norms[key]=list(map(float,found[-1]))
        if not heading or not norms: continue
        corrections=re.findall(rf"\s+displacement\s+({NUMBER})\s+({NUMBER})\s+({NUMBER})",text)
        record={"step":int(heading[1]),"printedTime":float(heading[2]),
                "status":"failed" if "failed to converge" in text else "converged" if "converged at time" in text else "in_progress_or_truncated",
                "iterationsInAttempt":len(corrections),"failed":"failed to converge" in text,
                "iterationCapReached":"Max nr of reformations reached" in text,
                "zeroLineSearchWarnings":text.count("Zero linestep size"),
                "lastNormsInitialCurrentRequired":norms}
        if "displacement" in norms:
            record["lastDisplacementCorrectionL2M"]=math.sqrt(norms["displacement"][1])
            record["requiredDisplacementCorrectionL2M"]=math.sqrt(norms["displacement"][2])
        attempts.append(record)
    return {"purpose":"Native iteration diagnostics only; no acceptance or physical calibration",
            "inputFileSha256":raw,"stateAtRead":state["state"],"readAtUtc":datetime.now(timezone.utc).isoformat(),
            "lastAcceptedNormalizedTime":state.get("lastConvergedNormalizedTime"),
            "wholeLogRead":size<=limit,"logBytesAtRead":size,"maximumReadBytes":limit,
            "attemptsInReadWindow":len(attempts),"failedAttemptsInReadWindow":sum(row["failed"] for row in attempts),
            "iterationCapFailuresInReadWindow":sum(row["iterationCapReached"] for row in attempts),
            "lastAttempts":attempts[-12:]}


if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--folder",required=True,type=Path)
    parser.add_argument("--output",type=Path,help="Optional separate diagnostic JSON; native evidence is never changed")
    args=parser.parse_args();result=analyze(args.folder)
    if args.output: args.output.write_text(json.dumps(result,indent=2))
    summary={k:v for k,v in result.items() if k!="lastAttempts"}
    summary["lastAttempt"]=result["lastAttempts"][-1] if result["lastAttempts"] else None
    print(json.dumps(summary))
