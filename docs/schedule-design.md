# Candlelight 0.3: a day of light

The daily graph is the main work surface. A monitor strip switches only the
selected screen's colors; a shared timeline owns the hours. Existing hours become
the shared starting point, with independent values for each monitor. A screen can
detach its hours or copy another screen's colors explicitly.

Palette: ink `#181E23`, raised slate `#222C33`, field `#2C3941`, paper `#E9E0D1`,
secondary `#A8B6B9`, candle `#E6BC78`. Muted copper distinguishes brightness from
temperature, but labels and line styles carry the meaning without color. The
graph's daylight band and solar markers encode actual local sunrise/sunset.
No decorative gradients, card grid, all-caps labels or autonomous animations.

Typography: Bahnschrift Semilight for the title and current profile; Segoe UI for
native controls, readable numbers and Portuguese labels. All content aligns left.
The existing small candle icon stays in the title bar and tray.

```
Candlelight                         profile now             active
[ Internal display ] [ OLED ]       shared hours / own colors

Daily schedule                         Temperature | Brightness
  sunrise        today's editable curve             sunset
  00     03     06     09     12     15     18     21     24
  + point   copy colors   undo         transition: 15 minutes

Selected point                    Color and brightness for this display
Clock / sunrise / sunset          precise value + slider
Time or solar offset              pure red / presets

Location and today's solar times                 filter pointer
```

The plan was revised from a generic settings dashboard: the graph receives most
of the space, while monitor selection remains compact. No repeating statistic
cards. Global timing is visibly separate from the selected screen's color editor.
Graph changes remain a draft during dragging and save on release; Escape cancels,
Ctrl+Z undoes. Numeric editors and point selection provide keyboard equivalents.
The selected point states that its time starts the transition; dotted stems link
target handles to the actual interpolated curve where needed.

Preserve the user's four clock points and 15-minute duration in migration. Show
daily solar markers immediately after configuring the chosen city; binding a
point to sunrise/sunset is an explicit editing action and can include an offset.
Solar calculations are local, date-aware and use the location's time zone, so
daylight-saving changes are included. Missing polar events use that point's
visible fallback clock time and show a status message.

References: [f.lux daily schedule](https://justgetflux.com/news/pages/v4/welcome/),
[NOAA solar equations](https://www.gml.noaa.gov/grad/solcalc/solareqns.PDF),
[Póvoa de Varzim coordinates](https://www.geonames.org/8014137/p-voa-de-varzim.html).

## 0.4: repeatable solar curves and a disposable day

Keep the same six-color palette and native type scale. The distinctive work
surface remains the light curve, with two directly draggable Bézier handles in
the transition editor. Avoid squeezing more tiny controls under the graph:
use a focused editor for the selected transition and a two-phase solar setup.

```
[Só hoje] [Programa]   volta ao programa às 00:00   [Retomar programa]
O teu dia                           [Temperatura] [Brilho]
                 existing daily curve
[Ciclo solar…] [+ Ponto] [Curva…] [Desfazer]     duração por defeito
                 selected point + monitor color
```

The solar setup names the two real events, with start offset and duration for
each. Colors are seeded from each monitor's existing coldest/warmest targets;
the user can then edit them independently. A preview explains today's actual
start/end times. Selecting a different calendar day previews the moving times.
Per-point duration and curve override the global default only when requested.

Review: two generic save/cancel forms would obscure whether a change repeats.
Instead scope is always visible above the main graph. `Só hoje` is the default
for quick changes, while `Programa` explicitly edits the recurring plan. The
temporary layer is persisted separately for restart recovery, expires at local
midnight even while hidden or suspended, and never overwrites the base program.
An explicit `Retomar programa` discards it early. Dialogs have a local draft so
Cancel/Escape does not change either layer. Undo never revives an expired day.
