# Properties, parameters and configuration values

A **property** belongs to a component or assembly definition. Edit it in that source's editor and publish a revision. Importers read the property; they cannot override it. Bump-stop material is always a component property. Material estimates and manually entered coefficients are source data, not Garage settings.

A **parameter** is a setting declared by a definition, with an ID, name, units, bounds and a supported behavior binding. Its definition does not contain a selected value for its own parameter.

A **configuration value** belongs to the immediate importer. Assembly Editor configures parameters of its direct child instances. It can assign a literal or forward one of the assembly's own parameters. Every intervening assembly must explicitly forward. Garage imports a complete-car assembly and supplies only that assembly's declared parameters.

For example, a spring declares `rate`. Its corner assembly can fix that instance at 3 kgf/mm, or forward a declared `springRate` parameter. If that corner's importer fixes `springRate`, a higher-level importer cannot override it. Exposing the setting in Garage requires explicit forwarding through the complete hierarchy.

Component Editor offers a property/parameter choice for spring rate and torsion shear modulus/outside diameter/inside diameter. Declare a parameter and bind the field to its ID. Numeric declaration units are `kgf/mm`, `Pa`, and `m`. When dimensions are parameters, torsion stiffness is calculated from the configured geometry and modulus; it cannot also be pinned to a prescribed rate.

Assembly Editor's **Parameters / imports** tab holds its declarations and direct-child assignments. The child contract inspection is read-only. Enter one numeric literal, named choice, or owner parameter ID. The **temporary preview importer** supplies values for inspection and publication validation. Those values remain in the workspace draft; they are not published as source defaults.

Garage's complete-car parameter controls configure the root assembly. Opening that car in Assembly Editor passes its settings into the temporary preview context. Frozen exports retain the original source definitions and root configuration, and reconstruct each resolved instance configuration through the saved import chain.

Existing spring/bar configurations migrate to new scoped source assets and explicit forwarding. Original assets and Garage revisions remain available. The migration preserves selected rates, tube dimensions and effective shear modulus; it does not insert saved car values into component definitions. A later parameter-to-property source change is a conflict that needs review.

Component Editor's **Adjustment bindings** connects an authored adjustment to a parameter. Continuous positions use the unit `fraction`, bounded within 0–1. Discrete mounting holes use actual named choice IDs; they cannot be interpolated. An unbound adjustment keeps a fixed source position. The stored geometry's nominal/reference position is a property used to calculate movement; it is not a selected value for an owner's parameter.

Assembly Editor's **Owner equipment slots**, **Equipment options**, and **Option configurations** define hardware choices for direct children. Each option references approved hardware and supplies the child parameters as literals or explicit forwarding. Binding a slot to an assembly choice parameter exposes that choice to its importer. Leaving that binding blank keeps the equipment fixed as an assembly property. Different options can expose different mechanical settings; an adjustable damper may use a perch parameter that the stock damper does not have.

Named-choice declarations use stable IDs and labels, such as `short=Short arm; long=Long arm`. A child's choice parameter can receive a literal ID or a forwarded compatible named-choice parameter. Numeric indices never stand for mounting holes or hardware choices.

Garage edits only the complete-car root configuration. Numeric controls display GPa, mm, percent or kgf/mm as appropriate, while saved values retain the declared contract units. Settings used only by other equipment options are marked inactive for the current hardware. Their explicit values remain saved so switching equipment can reuse them. Alignment and ride height remain calculated from mechanical settings.

Existing mechanical adjustment and equipment selections migrate into a new source hierarchy with explicit forwarding and root configuration values. Original source assets, revision pins, previous Garage revisions and old frozen results remain available. New version-2 cars have no descendant equipment/adjustment override arrays. Invalid legacy drafts are retained for recovery; migration never silently clamps an invalid setting.

Frozen bundles include approved version-2 equipment alternatives as well as the selected configuration. An independent imported clone can select those alternatives without depending on the original live library. Grouping and mirroring preserve the explicit configuration boundaries.

FEA material/curve publication remains gated by numerical verification. Editing a recipe does not recompute a measured CSV curve, and an incomplete FEA run cannot publish a curve for a newly selected material.
