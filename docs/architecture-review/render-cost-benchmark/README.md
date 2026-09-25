# R19 render-cost experiment

This project is measurement infrastructure, not a second product renderer.

It links the production LayeredSpriteRenderer.cs and records two same-machine observations:

1. a transparent WPF window under RenderMode.Default and RenderMode.SoftwareOnly;
2. repeated production UpdateLayeredWindow calls, including CPU/wall time, process memory deltas and GDI/USER handle deltas.

The benchmark intentionally has no timing pass/fail threshold. Hosted Windows runner numbers are useful for same-run comparison but cannot prove behavior on a physical mixed-DPI desktop, a specific GPU/driver, or Remote Desktop.

R19 only changes production rendering if repeated measurements show a material cost and the candidate also preserves transparency, DPI behavior and handle lifetime. Otherwise the existing renderer and SoftwareOnly setting remain.
