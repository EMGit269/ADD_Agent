---
name: system-csharp-mode
description: System skill for C# priority mode. Use when core Grasshopper logic must stay inside one or a few C# Script components, with only Params and Display helpers around them.
---

# System Skill: C# Priority Mode

Use this skill whenever the layout mode is `C# 优先`.

## Core Position

- Put all core modeling logic in one or a few C# Script components.
- Do not replace core geometry, math, data-tree, transform, curve, surface, Brep, mesh, or list-processing logic with ordinary Grasshopper battery chains.
- Non-script components are helpers only, and should come from `Params` or `Display`.

## What Counts As Helper Components

Allowed helper roles:

- user-facing inputs such as sliders, toggles, text panels, value lists, and geometry params
- outputs and result display panels
- preview and inspection helpers
- lightweight debugging helpers

Not allowed as core-logic substitutes:

- long native GH chains for geometry construction
- native math or list-processing graphs that should live in the script
- data-tree reshaping graphs used only to work around missing script logic

## Recommended Workflow

1. Restate the modeling goal in geometric and data-flow terms.
2. Decide the script boundary before touching the canvas.
3. For a new user requirement, prefer one new C# Script component that owns the new responsibility, instead of rewriting a healthy existing script.
4. Define exact inputs, outputs, and type hints for the C# Script.
5. Keep helper components outside the script minimal and obvious.
6. Group each C# Script with its own sliders, panels, geometry params, value lists, preview, and debug helpers whenever practical.
7. Implement the main logic in the script body.
8. Verify outputs by value, type, null-state, and structure, not only by "no runtime error".

## Code Rules

- The generated body should be valid for the `RunScript` method body only.
- Do not include `using`, class declarations, full templates, or a custom `RunScript` signature in body-only output.
- Match input names and port order exactly. For outputs, keep the requested business labels aligned with the declared ports, but assign values in the body to the actual generated output variables (`b`, `c`, `d`...) rather than inventing custom output variable names.
- Prefer strongly typed inputs such as `double`, `int`, `bool`, `string`, `Point3d`, `Vector3d`, `Curve`, `Brep`, `Mesh`, and `Plane`.
- Validate and clamp invalid or extreme inputs early.
- Assign every output explicitly.
- Prefer simple, deterministic code over clever or fragmented code.
- Keep the number of script components low; split only when the logic is genuinely clearer.

## Editing Rules

- When updating an existing C# Script, preserve the established port contract unless the task explicitly requires a port change.
- Treat an existing correct C# Script as stable by default. If it has no bug and already matches its current responsibility, prefer leaving it untouched and adding a new script beside it for additive requirements.
- If a port change is required, update the port design first, then fix the body to match the new signature.
- Do not rewrite an entire script template to work around a local body issue.
- Edit an existing script only when it is necessary for correctness, a shared interface change, or a clearly better script boundary that simplifies the overall graph.

## Grouping Rules

- When creating a C# Script and its dedicated sliders or helper inputs in one step, pass `group_name` so the script and helpers land in one Group.
- If dedicated helpers are added later, add them to the script's existing Group with `manage_gh_groups`.
- Do not group unrelated upstream/downstream logic just because it is nearby; the Group should represent the script's local control surface and immediate helper outputs.

## Plan-Mode Expectation

In `Plan` mode, the plan must still describe a C#-centered solution:

- identify which logic lives in the C# Script
- identify which `Params` and `Display` helpers surround it
- avoid battery-heavy implementation plans
- explain why the chosen script boundary is appropriate
- prefer steps where one step maps to one coherent C# Script component plus minimal helpers
- if the task is simple, do not split it into multiple script steps just for symmetry

## Output Preference

- Prefer one main geometry output plus one report/debug output when possible.
- For multiple results, prefer `List<T>` outputs over exploding the graph into many helper branches.
- Keep the final graph readable from left to right: inputs, script core, outputs.
