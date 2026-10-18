# Source chassis and configured cars

Each new stock GR86/BRZ complete vehicle contains one visible chassis component: two rails, separate front/rear overhangs, axle reference sections, and a rack crossmember behind the front axle. Corner mounting pieces are rigid brackets attached to the corresponding chassis section. Rack housing/guide supports follow the crossmember.

Edit the chassis in **Component Designer → Mechanics**. Its dimensions and rigidities are source properties, immutable to importing assemblies and Garage. Publishing a compatible source revision updates dependent cars atomically; saved per-car parameter values remain unchanged. Prior source revisions and frozen exports remain recoverable.

Chassis length is undeformed axle-reference-plane spacing, initially2.575m. Moving a mechanical wheel adjustment can change the measured wheelbase slightly; the length is the mounting reference spacing, not an extra prescribed loaded-wheel measurement. Overhangs are independent. The rack crossmember is initially160mm behind the front axle.

The chassis is an unshearable, inextensible equivalent beam with three constant-curvature elastic coordinates: vertical-plane bending, lateral-plane bending and longitudinal-axis twist. Its centerline arc length is preserved; no axial compression/extension spring is introduced. Bending can shorten the straight-line chord geometrically. The actual mounting frames and wheel/rack geometry move with the solved beam sections. This reduced model omits local panel flexibility and higher bending modes.

Estimated initial source properties:

| Property | Rigidity |
|---|---:|
| Vertical-plane bending, EI_y |4.62MN·m²|
| Lateral-plane bending, EI_z |6.00MN·m²|
| Long-axis torsion, GJ |2.95MN·m²|

These are generic equivalent-body engineering estimates, not measured GR86/BRZ shell values. The torsion value corresponds to about20,000N·m/degree at2.575m. Pure end-couple rates are EI/L and GJ/L in N·m/rad; a simply-supported center-load comparison gives48EI/L³. These conventions prevent confusing rigidity with an arbitrary mounting-point N/mm rate.

The source provenance retains the research and manufacturer links: [equivalent vehicle beam study](https://journals.sagepub.com/doi/10.1177/0036850420927204), [body stiffness study](https://www.extrica.com/article/21171), [Toyota GR86 release](https://newsroom.toyota.eu/world-premiere-of-all-new-gr-86/), and [Subaru BRZ release](https://s3.amazonaws.com/subarumedia.iconicweb.com/mediasite/attachments/22MY_BRZ_Release_-_FINAL_UPDATED.pdf). Manufacturer percentage increases do not establish an absolute measured stiffness for this component.
