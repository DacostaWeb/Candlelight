# Candlelight icon

The selected design is option 4 with the smaller orange/gold flame, a visible
dark wick and a short cream candle tip. `candlelight-source.png` is the unchanged
approved image from the built-in image generation tool; its edit prompt is saved
in `generation.json`.

`scripts/export-icon.ps1` removes unused transparent canvas padding, centers
the artwork and exports alpha-preserving PNG and ICO sizes. The candle/flame
proportions and colors stay intact. The ICO contains 16, 20, 24, 32, 40, 48, 64,
96, 128 and 256 pixel images; the 256 pixel image uses PNG compression.

The root `favicon.ico` is shared by both application executables, their windows,
the notification-area icons and the installer. `favicon.png` is the repository
logo. Exported PNG frames are kept in `sizes/`.

```powershell
./scripts/export-icon.ps1 -OutputDirectory ./assets/icon/sizes
Copy-Item ./assets/icon/sizes/favicon.ico, ./assets/icon/sizes/favicon.png .
```
