# Candlelight interface

Version 0.3 puts the daily graph first: a monitor strip, temperature/brightness
curves, shared timing, one global transition duration and a selected-point editor.
The sun markers show the chosen location's actual calculated day. The layout has
been rendered and checked at 150% DPI, at full and compact widths. See
[the design plan and critique](schedule-design.md) and [the schedule guide](schedules.md).

The notes below document the earlier layouts.

A quiet, readable Windows control panel for adjusting light at night. The monitor
rail stays visible while the selected monitor's settings scroll independently.
Presets are immediately below monitor selection; manual values combine sliders
with exact numeric input. The schedule is a table of clock time, temperature,
brightness and transition minutes rather than two sunrise/sunset controls.

Dark palette: surface `#171411`, monitor rail `#211B17`, text `#DFCEC3`, supporting
text `#B6A699`, warm accent `#D8B46E`, divider `#44372C`. The existing Light/System
theme settings also receive matching readable surfaces. Segoe UI uses 28 px for
the selected monitor, 19 px for sections, 14 px for controls and 13 px for help.
All content is left aligned. The monitor list and numeric controls carry the
hierarchy; no decorative diagrams or motion compete with them.

The 0.2 prototype reduces this to one control window: a monitor picker, two
sliders with precise numeric inputs, an explicit red-only checkbox, quick/saved
presets and a short list of daily time points. The palette shifts to slate
`#171B1D` / `#262E2F`, pale warm text `#EDDECB`, supporting text `#ABB7B1` and
slider accent `#D6B278`. Segoe UI remains native and scales with Windows DPI.
Brightness and temperature have different ranges, clear units and keyboard
controls. Plain rows replace the former monitor rail and multiple settings tabs.
