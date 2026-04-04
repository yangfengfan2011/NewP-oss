# Asset Disposition Matrix

Use one row per old asset or asset class.

| Asset | Category | Current location | Decision | Target module/area | Why | Preconditions | Owner |
| --- | --- | --- | --- | --- | --- | --- | --- |

## Decision definitions

- `Retire`: stop carrying it forward
- `Rewrite`: reimplement natively in the new platform
- `Wrap`: keep temporarily behind a compatibility seam
- `Import`: convert data or metadata into new-platform form
- `Postpone`: not in the first wave

## First-wave guardrail

Only `Rewrite` and carefully chosen `Import` items should dominate the first wave.
`Wrap` should be used sparingly and must have an exit plan.

