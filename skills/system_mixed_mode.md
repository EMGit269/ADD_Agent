---
name: system-mixed-mode
description: System skill for mixed mode. Use when the agent should deliberately choose between native Grasshopper batteries and C# Script based on complexity, clarity, and maintainability.
---

# System Skill: Mixed Mode

Use this skill whenever the layout mode is `混合模式`.

## Core Position

- Choose the cheapest representation that is still clear, stable, and maintainable.
- Use native Grasshopper batteries for simple, visual, parameter-driven logic.
- Use C# Script for logic that becomes awkward, repetitive, or brittle as a battery chain.
- Do not force everything into batteries, and do not force everything into C#.

## Prefer Native Grasshopper For

- direct parameter wiring
- small arithmetic and remapping steps
- standard transforms
- simple list handling that stays readable
- common built-in geometry operations with clear visual flow
- user-facing graph sections the user is likely to inspect and tweak manually

## Prefer C# Script For

- loops and indexed construction
- branching logic
- repeated module generation
- custom geometry algorithms
- compact handling of dense derived values
- logic that would otherwise require a long or fragile battery chain
- data shaping that is much clearer in code than on the canvas

## Workflow

1. State the modeling goal and data flow first.
2. Mark which subproblems are simple GH and which are better as C#.
3. Keep native GH sections grouped and coherent.
4. Keep C# sections few and purposeful.
5. Group each C# Script with its own sliders, panels, geometry params, value lists, preview, and debug helpers whenever practical.
6. Wire the native GH and C# sections through clear typed interfaces.
7. Validate both graph readability and output correctness.

## Decision Rules

- If a native GH solution stays short, obvious, and editable, prefer native GH.
- If a native GH solution becomes long, repetitive, or hard to reason about, move that part into C#.
- Do not create a C# Script for trivial sliders, panels, or elementary math.
- Do not build sprawling native graphs to avoid writing a small, clear script.

## C# Boundaries In Mixed Mode

- A C# Script should own a coherent chunk of logic, not random leftovers.
- Inputs to the script should be typed and minimal.
- Outputs from the script should be few, meaningful, and easy to inspect.
- Keep script responsibilities local so the surrounding GH graph still reads cleanly.
- The script's local control surface should stay visually local: group the C# Script with its dedicated sliders and immediate helper components, but keep unrelated native GH logic in its own group.

## Plan-Mode Expectation

In `Plan` mode, the plan should explain:

- which steps stay native GH
- which steps become C# Script
- why each boundary was chosen
- how the user-facing controls remain easy to understand

The plan should not default to all-battery or all-script unless the task clearly demands it.

## Verification

- Check outputs for nulls, empties, wrong types, and wrong structure.
- Verify that the chosen split actually reduced graph complexity.
- If a script exists only to hide trivial work, move that logic back to native GH.
- If a battery graph became dense and hard to maintain, consolidate the right part into C#.
