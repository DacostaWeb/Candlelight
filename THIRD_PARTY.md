# Credits

Candlelight is a fork of [LightBulb](https://github.com/Tyrrrz/LightBulb), copyright
2017–2026 Oleksii Holub, under the MIT license in [License.txt](License.txt).

The exact-zero gamma channel handling and regression tests in `GammaColor.cs`,
`GammaRamp.cs`, `GammaColorSpecs.cs` and `GammaRampSpecs.cs` are adapted from
[Kari Pesonen's LightBulb Ahead](https://github.com/karipesonen/LightBulb-Ahead),
revision `5d834427dc1094d38b619cc14198d58a6769814a`, under the same MIT license.
Candlelight uses smaller update thresholds for precise monitor controls while
preserving Ahead's forced updates at quantized zero-channel boundaries.
