"""Read-only deformation-gradient sampling of the pinned benchmark plot data.

Reports invalid sampled elements instead of repairing or changing a solve. The
same reference points as the initial quadratic-tet check are sampled. This is a
diagnostic, not a proof of global element bijectivity or a publication gate.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import xml.etree.ElementTree as ET

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("plot_diagnostic", ROOT / "tools/fea-plot-diagnostic.py")
plot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(plot)


def sample(model, ids, coordinates, displacement):
    lookup = {int(n): i for i, n in enumerate(ids)}
    bodies = model.findall("Mesh/Elements[@type='tet10']/elem")
    if not bodies:
        raise ValueError("Expected quadratic tetrahedra")
    element_ids = [int(e.attrib["id"]) for e in bodies]
    vertices = np.array([[lookup[int(n)] for n in e.text.split(",")] for e in bodies])
    reference = coordinates[vertices]
    deformed = (coordinates + displacement)[vertices]
    edges = [(0,1),(1,2),(0,2),(0,3),(1,3),(2,3)]
    gradients = np.array([(-1.,-1.,-1.),(1.,0.,0.),(0.,1.,0.),(0.,0.,1.)])
    points = [(.25,) * 4]
    points += [tuple(.5854101966249685 if i == k else .1381966011250105 for i in range(4)) for k in range(4)]
    points += [tuple(1. if i == k else 0. for i in range(4)) for k in range(4)]
    points += [tuple(.5 if i in edge else 0. for i in range(4)) for edge in edges]
    minimum = float("inf")
    worst = None
    invalid = set()
    quadrature_minimum = float("inf")
    for index, point in enumerate(points):
        derivatives = np.array([*(gradients[i] * (4 * point[i] - 1) for i in range(4)),
                                *(4 * (gradients[a] * point[b] + gradients[b] * point[a]) for a,b in edges)])
        before = np.linalg.det(reference.transpose(0,2,1) @ derivatives)
        after = np.linalg.det(deformed.transpose(0,2,1) @ derivatives)
        if np.any(before <= 0) or not np.all(np.isfinite(before)):
            raise ValueError("Reference mesh contains an invalid sampled Jacobian")
        ratio = after / before
        if not np.all(np.isfinite(ratio)):
            raise ValueError("Deformed mesh contains a non-finite sampled Jacobian")
        if 1 <= index <= 4:
            quadrature_minimum = min(quadrature_minimum, float(np.min(ratio)))
        at = int(np.argmin(ratio))
        if ratio[at] < minimum:
            minimum = float(ratio[at])
            worst = {"elementId": element_ids[at], "barycentric": point,
                     "referenceCentroidM": reference[at,:4].mean(axis=0).tolist()}
        invalid.update(element_ids[i] for i in np.flatnonzero(ratio <= 0))
    return {"minimumSampledRelativeJacobian": minimum,
            "minimumFourPointQuadratureRelativeJacobian": quadrature_minimum,
            "nonpositiveSampledElementIds": sorted(invalid), "worstLocation": worst,
            "tetrahedra": len(bodies), "locationsPerTet": len(points),
            "claim": "Sampled deformation diagnostics only; does not publish or repair a curve"}


def self_check():
    model = ET.fromstring('<febio_spec><Mesh><Elements type="tet10"><elem id="7">1,2,3,4,5,6,7,8,9,10</elem></Elements></Mesh></febio_spec>')
    corners = np.array([(0.,0.,0.),(.02,0.,0.),(0.,.02,0.),(0.,0.,.02)])
    edges = [(0,1),(1,2),(0,2),(0,3),(1,3),(2,3)]
    xyz = np.concatenate([corners, [(corners[a]+corners[b])/2 for a,b in edges]])
    transform = np.array([1.12,1.12,.7])
    state = sample(model, range(1,11), xyz, xyz*(transform-1))
    if abs(state["minimumSampledRelativeJacobian"] - float(np.prod(transform))) > 1e-12 or state["nonpositiveSampledElementIds"]:
        raise ValueError("Independent affine volume check failed")
    inverted = sample(model, range(1,11), xyz, xyz*np.array([-2.,0.,0.]))
    if inverted["nonpositiveSampledElementIds"] != [7]:
        raise ValueError("Inverted-element diagnostic did not reject reflection")
    print(json.dumps({"status":"Independent affine volume and reflected-element checks passed"}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--folder", type=Path)
    parser.add_argument("--self-check", action="store_true")
    args = parser.parse_args()
    if args.self_check:
        self_check()
    if args.folder is None:
        if not args.self_check: parser.error("Specify --folder or --self-check")
        raise SystemExit(0)
    journal = json.loads((args.folder / "process.json").read_text())
    model, ids, xyz, u, info = plot.read_plot(args.folder, journal.get("lastConvergedNormalizedTime", 1.))
    result = sample(model, ids, xyz, u) | info
    result["maximumFixtureErrorM"] = plot.check_fixture(model, ids, u, info["normalizedTime"])
    (args.folder / "mesh-diagnostic.json").write_text(json.dumps(result, indent=2))
    print(json.dumps(result))
