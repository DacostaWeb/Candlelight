# Solar controls

Keep the approved LightBulb-based engine, monitor rail and control layout.
The new controls belong to each monitor's existing day/night mode.

- Palette: existing surface #171411, rail #211B17, text #DFCEC3, muted #B6A699,
  temperature accent #D8B46E; dashed brightness trace #B6A699. No new theme or animation.
- Type: Segoe UI, existing 19 px section headings and 13/14 px labels.
- Layout: monitor rail | selected monitor, normal-color switch, mode, sunrise
  and sunset curves, day/night values and solar location. Keep labels left aligned.
- Each curve shows its actual start/end times, color and brightness endpoints.
  Sunrise starts at sunrise; sunset finishes at sunset. Both default to 120 min.
- Drag two curve handles or choose a named curve. Duration changes the interval;
  shaping the curve changes how quickly the same endpoints are reached.
- Normal colors bypass only the selected monitor's filter; resuming evaluates
  the current solar time without replacing the saved program.

Brief review: two event plots are more useful here than a new dashboard or a
decorative all-day chart. They expose the exact intervals the user wants to edit,
while retaining the familiar per-monitor settings and gamma engine.

Visual critique after rendering the compiled interface: the first layout put
the curve endpoints below the fold on a 700 px window. Moving monitor renaming
into an expander and wake details into tooltips makes both curves and duration
inputs visible together. Checked 1080 × 700 and 880 × 600; the content scrolls
at compact sizes. Preset buttons, endpoint inputs and location remain reachable.
Compiled preview verification also checks handle bindings, curve presets,
duration labels, serialization and normal-color isolation/resumption.
