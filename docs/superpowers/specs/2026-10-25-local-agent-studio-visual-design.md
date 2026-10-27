# Local Agent Studio visual design: Precision

Status: the user prefers Precision; use it as the base visual direction.
Product behavior and testing remain defined by the [studio design](2026-10-25-local-agent-studio-design.md)
and [implementation plan](../plans/2026-10-25-local-agent-studio.md).

## Reference and scope

Use direction A in the [interactive exploration](../mockups/studio-visual-directions.html).
It establishes dark surfaces, compact information density, persistent navigation
and coordinated graph, timeline and inspector panels. Fieldwork is not a second
theme requirement. All executions and metrics in the exploration are simulated.

This selection is approval of direction, not a requirement to copy every pixel.
The refinements below are implementation defaults for readability and usability.

## Workspace composition

- A restrained top bar identifies Studio, local connection status and primary
  navigation. Connection indicators must reflect actual state in the product.
- A persistent left sidebar provides entity/run navigation and the current
  in-memory session's executions.
- The run heading identifies the entity, run, outcome and available actions.
  Duration, model calls, reported usage and available cost are secondary context.
- The investigation area gives the graph and timeline the main column, with a
  detail inspector alongside. Panels can resize/collapse as space requires.
- Graph, timeline and inspector share selection by actual IDs. Updates preserve
  scroll, zoom and selected invocation.
- The catalog uses compact rows, the input screen pairs JSON editing with its
  contract, and comparison uses aligned baseline/candidate details.

## Token baseline

| Token | Reference value | Purpose |
| --- | --- | --- |
| Base | `#11141a` | Application background |
| Surface | `#171b23` | Sidebar, panels and controls |
| Elevated | `#1c222d` | Active rows and secondary surfaces |
| Border | `#2b3341` | Quiet separators |
| Primary text | `#e1e7f0` | Main readable content |
| Secondary text | `#98a4b6` | Labels and supporting detail |
| Accent | `#a7afff` | Actions, selection and focus |
| Accent surface | `#252b47` | Selected navigation/controls |
| Success | `#86d9b5` | Completed/valid states |
| Warning | `#edbf7b` | Retry/partial-result attention |
| Error | `#f5a3a7` | Failed/rejected states |

Verify contrast in the real components; adjust combinations when needed for WCAG
AA. Use status labels/icons alongside color. Selection uses the accent, rather
than implying that every selected node has a warning.

## Typography, geometry and interaction

- Use DM Sans or an equivalent readable sans-serif for UI and IBM Plex Mono or
  an equivalent monospace for IDs, code and numeric inspection. Bundle licensed
  font assets locally; no remote-font dependency at runtime.
- Target 13 px body/control text, 12 px inspected code and at least 11 px metadata.
  Do not obtain density by copying the mockup's smallest labels everywhere.
- Use a 4 px spacing base, predominantly 8/12/16/24 px gaps, subtle 5–7 px radii,
  thin separators and restrained shadows. Keep payloads selectable and readable.
- Prefer familiar, consistently sized line icons with accessible labels; no
  decoration that competes with execution evidence.
- Provide complete hover, focus, selected, disabled, loading, empty, unavailable,
  running, partial, failed and cancelled states using shared UI primitives.
- Use short transitions for state/selection changes, respect reduced motion and
  avoid animation on every streamed token.
- Optimize desktop at 1440x900 and 1024x768. Collapse or rearrange panels when
  required; no full mobile investigation workspace is required initially.

## Acceptance

Inspect the real Angular application with realistic long IDs, nested payloads,
parallel branches, repeated invocations and unavailable usage/cost. Review the
catalog, input, investigation and comparison views, including empty/error states,
keyboard focus and panel resizing. Functional tests alone do not establish visual
quality. Keep the agreed three browser E2E journeys; do not add a screenshot
regression matrix as a substitute for visual review.
