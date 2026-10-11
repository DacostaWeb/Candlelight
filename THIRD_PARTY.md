# Credits

The offline sunrise/sunset calculation in `Candlelight.Engine/SolarTimes.cs`
implements the [NOAA general solar position equations](https://www.gml.noaa.gov/grad/solcalc/solareqns.PDF).
City search uses the [Open-Meteo geocoding API](https://open-meteo.com/en/docs/geocoding-api),
whose location data is supplied by [GeoNames](https://www.geonames.org/) under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). It is used only when
the user requests a city search; daily solar calculation is local. The configured
Póvoa de Varzim coordinates are from [GeoNames](https://www.geonames.org/8014137/p-voa-de-varzim.html).

Candlelight is a fork of [LightBulb](https://github.com/Tyrrrz/LightBulb), copyright
2017–2026 Oleksii Holub, under the MIT license in [License.txt](License.txt).

The exact-zero gamma channel handling and regression tests in `GammaColor.cs`,
`GammaRamp.cs`, `GammaColorSpecs.cs` and `GammaRampSpecs.cs` are adapted from
[Kari Pesonen's LightBulb Ahead](https://github.com/karipesonen/LightBulb-Ahead),
revision `5d834427dc1094d38b619cc14198d58a6769814a`, under the same MIT license.
Candlelight uses smaller update thresholds for precise monitor controls while
preserving Ahead's forced updates at quantized zero-channel boundaries.

The original C# Night Light state controller uses the reverse-engineered
[CloudStore format documented by win-nightlight-cli](https://github.com/kvnxiao/win-nightlight-cli/blob/main/docs/nightlight-registry-format.md)
as a schema reference. It preserves the strength and schedule and only changes
the active state during protected handovers, restoring the original active
state on exit. Unknown state schemas are never rewritten.
