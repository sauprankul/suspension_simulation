# Garage entry management

Choose Garage from the main menu. New creates an entry; select it, edit its name/notes and use Save changes. Duplicate makes an independent copy. Search filters names. Archive retains history; enable Archived entries and use Restore to recover an entry. Unsaved name/notes edits are retained while switching entries within this session; Save changes is required for durable storage.

Data lives in ignored `library/garage/<id>/revisions`. Each save publishes a new complete JSON revision atomically without overwriting history. Stale edits are rejected rather than overwriting a newer revision. This local library is separate from committed synthetic vehicle packages.

This first slice saves Garage drafts. Entries without resolved physical assemblies show Needs assembly and cannot run. Component equipment choices, bounded physical adjustments, static-equilibrium validation and visualizer Garage selectors remain pending; the current legacy visualizers are separate.

Validation: 85/85 regression checks pass, including independent entries, persistence/reopen, duplicate, archive/restore, append-only history, stale-save rejection and safe IDs. `--garage-proof` exercises New, Save, Duplicate, rename, archive/restore, search and navigation with an isolated library; report and inspected capture are in `artifacts/garage-proof`.
