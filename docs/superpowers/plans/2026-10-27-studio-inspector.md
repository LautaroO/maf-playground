# Studio contextual inspector implementation plan

**Goal:** Keep the graph beside a useful contextual inspector, with an option to hide the graph and use the full workspace width.

**Approved design:** The conversation approved a resizable right inspector, automatic focused content loading, Input/Output/Details tabs, integrated technical evidence, and run information when no operation is selected. The user added graph hiding. The graph remains mounted so selection and viewport survive restoration; timeline and execution output stay available below when hidden.

**Architecture:** Deterministic Angular presentation and existing payload API. No backend, model, dependency, persistence, or discovery changes.

## Tasks

- [x] Replace RunWorkspace's width slider with a draggable, keyboard accessible separator. Bound width to preserve graph space. Add Hide graph / Show graph and responsive full-width inspection. Remove duplicate Run inspector. Tests cover resize limits and selection/graph preservation.
- [x] Unify SpanInspector around the current operation or run. Tabs expose input, output/validation/errors and contextual details. Load only visible selected payloads automatically, cancel obsolete requests, and reject stale responses. Tests cover tab scope, redaction/unavailable states and selection races.
- [x] Update the existing three browser journeys and testing guide. Verify splitter interaction, hide/restore viewport, automatic evidence, keyboard accessibility, 1440/1024 layouts and one inspector scroll container.
- [x] Run frontend unit tests, types, format, contracts, production build and compiled browser journeys. Review complete diff/untracked files, scan credentials and update CLI assets without restarting the user's ephemeral host.

## Review focus

- Late content from another run or selection must never replace active content.
- Restoring the graph preserves selection, node positions, zoom and pan.
- Narrow layout has no horizontal document overflow or duplicate inspector scrollbars.
- Unknown evidence and capture gaps stay discoverable; never fetch whole journal payloads eagerly.
- Missing inputs/outputs and provider failures remain explicit without fabricating evidence.

**Verification:** 114 Angular tests, 2 proxy tests and the existing 3 compiled browser journeys passed. Frontend format, types, generated contracts and production build passed; CLI build copied assets without restarting the host. Final source review and tracked/untracked credential scan passed. The retained translation run was inspected in both split and hidden-graph layouts. No backend code or provider execution changed.
