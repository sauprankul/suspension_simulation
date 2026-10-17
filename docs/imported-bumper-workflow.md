# Install an imported bumper curve

This workflow uses a saved axial force–shortening curve. STEP/FEA curve generation remains under verification; it is not needed to use a measured or otherwise explicitly sourced CSV.

1. Open **Component Designer → Bump curve**. Load the UTF-8 CSV, choose its displacement/force units and contact-origin or installed-origin zero, and enter the attachment separation at that zero. Record the actual test/installation conditions and source classification.
2. Create/update the bumper from that curve, inspect the plot and supported range, then publish the component revision. The original CSV, units, datum, conditions and provenance are retained with the component.
3. Open the intended vehicle in **Assembly Editor**. Use **Duplicate as custom assembly** to make a separate vehicle while retaining the shared hardware references.
4. Open **Install bumper**. Choose the published curve and its two actual mounting references. These can be ports on parts or exported mounting references on nested assemblies. The component/assembly author must expose appropriate physical references if they are missing.
5. Choose an unused instance name and install the bumper. The complete change is validated before application; undo/redo operates on the bumper, both attachments and its optional equipment slot together. Publish the custom assembly.
6. In **Garage**, create or duplicate a setup, choose the custom vehicle assembly and save it. An exposed bumper appears as equipment. Other bumper assets require explicit approval in the assembly's equipment slot; the software does not infer physical compatibility from a name or hardness label.
7. Select that saved Garage entry in either visualizer. Both screens use the same accepted installed-equilibrium result. The comparison table includes bumper force, axial shortening, tangent and remaining curve range; select the bumper in 3D for its source/condition details.

The curve's attachment datum and fixture conditions must represent the chosen mounting references. A whole-part curve measured against strut-eye separation can legitimately use a different reference distance than the bumper's own free height, but these conventions cannot be interchanged silently. Mounting references determine the actual axial force path; the implementation does not substitute a spring motion ratio.

Publishing a compatible revised curve invalidates affected results and recomputes the complete A/B pair. Earlier exported results retain their frozen source revisions for replay. An incompatible attachment change or insufficient measured range rejects the new result and preserves the prior complete result. A curve's maximum shortening is a model-validity boundary, not a fabricated rigid stop.

## Verification checkpoint

The synthetic integration example exercises CSV publication, actual mount binding, arbitrary component-local origins/rotations, atomic installation/undo, custom assembly ownership, Garage save/reopen, shared 2D/3D results and displayed endpoint coincidence. Revised curves propagate through the production dependency watcher; frozen results replay independently; incompatible interfaces and unsupported ranges reject and recover.

The full regression suite passes **96/96** checks. Native `--bumper-integration-proof` reports success under `artifacts/imported-bumper-integration-ui`; the complete suite log is `artifacts/non-fea-integration-regression.log`. The example's bumper force changes from approximately 155.797 to 193.473 N, chassis heave from −13.4039 to −13.0746 mm, and FL camber from −0.277558° to −0.273530° after doubling its synthetic source force samples. These are implementation checks, not physical correlation or OEM vehicle data.
