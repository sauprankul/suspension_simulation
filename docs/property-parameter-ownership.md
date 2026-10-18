# Properties, parameters and configuration values

A **property** belongs to a component or assembly definition. Edit it in that source's editor and publish a revision. Importers read the property; they cannot override it. Bump-stop material is always a component property. Material estimates and manually entered coefficients are source data, not Garage settings.

A **parameter** is a setting declared by a definition, with an ID, name, units, bounds and a supported behavior binding. Its definition does not contain a selected value for its own parameter.

A **configuration value** belongs to the immediate importer. Assembly Editor configures parameters of its direct child instances. It can assign a literal or forward one of the assembly's own parameters. Every intervening assembly must explicitly forward. Garage imports a complete-car assembly and supplies only that assembly's declared parameters.

For example, a spring declares `rate`. Its corner assembly can fix that instance at 3 kgf/mm, or forward a declared `springRate` parameter. If that corner's importer fixes `springRate`, a higher-level importer cannot override it. Exposing the setting in Garage requires explicit forwarding through the complete hierarchy.

Component Editor offers a property/parameter choice for spring rate and torsion shear modulus/outside diameter/inside diameter. Declare a parameter and bind the field to its ID. Numeric declaration units are `kgf/mm`, `Pa`, and `m`. When dimensions are parameters, torsion stiffness is calculated from the configured geometry and modulus; it cannot also be pinned to a prescribed rate.

Assembly Editor's **Parameters / imports** tab holds its declarations and direct-child assignments. The child contract inspection is read-only. Enter a literal value or an owner parameter ID, never both. The **temporary preview importer** supplies values for inspection and publication validation. Those values remain in the workspace draft; they are not published as source defaults.

Garage's complete-car parameter controls configure the root assembly. Opening that car in Assembly Editor passes its settings into the temporary preview context. Frozen exports retain the original source definitions and root configuration, and reconstruct each resolved instance configuration through the saved import chain.

Existing spring/bar configurations migrate to new scoped source assets and explicit forwarding. Original assets and Garage revisions remain available. The migration preserves selected rates, tube dimensions and effective shear modulus; it does not insert saved car values into component definitions. A later parameter-to-property source change is a conflict that needs review.

The ownership correction is staged. The first slice covers scalar spring/bar parameters. Existing geometry adjustment and equipment-slot controls still use the earlier explicit-exposure system; moving continuous positions, named mounting holes and equipment choices to the same immediate-importer contract is Stage 8b. These older controls are not evidence that the entire ownership migration is complete. Alignment and ride height remain calculated from mechanical settings.

FEA material/curve publication remains gated by numerical verification. Editing a recipe does not recompute a measured CSV curve, and an incomplete FEA run cannot publish a curve for a newly selected material.
