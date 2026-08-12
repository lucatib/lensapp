# LensApp

A .NET MAUI app for Android and iOS that points the camera at a surface, zooms in on it, reads
the colour inside the reticle in real time, and tells you which **RAL Classic** shade it is.
The torch can be switched on to light the sample.

```
┌──────────────────────────────┐
│  Torch off  │ Hold │  2.4×   │   live preview, pinch or slide to zoom
│                              │
│            ┌──┐              │   the reticle marks the pixels being measured
│            └──┘              │
│      [ − ]══════[ + ]        │
├──────────────────────────────┤
│ ███  │ RAL 5015              │   best match, updated ~8×/second
│ #2A73│ Sky blue              │
│ B0   │ ΔE 1.4 · excellent    │
│      │ R 42  G 115  B 176    │
│      │ L* 46.6 a* -6.0 b*-35 │
│ [RAL 5019 ΔE 4.2] [RAL 5012] │   runners-up
│ Calibrate │ Reset WB │ Copy  │
└──────────────────────────────┘
```

## What it does

| Feature | How |
| --- | --- |
| Live preview | CameraX `Preview` (Android) / `AVCaptureVideoPreviewLayer` (iOS) |
| Zoom | Real camera zoom (`CameraControl.SetZoomRatio` / `AVCaptureDevice.VideoZoomFactor`), driven by pinch, a slider, ± buttons, or a double tap |
| Torch | `CameraControl.EnableTorch` / `AVCaptureDevice.TorchMode`, auto-released when the page goes away |
| Colour measurement | A centre patch (7 % of the short side) is averaged in linear light, with outlier pixels rejected |
| RAL match | Patch → CIE Lab → **CIEDE2000** against the RAL Classic table; best match plus three runners-up with their ΔE |
| Grey-card calibration | Point at something neutral, tap *Calibrate*; per-channel gains are stored in `Preferences` |
| Hold | Freezes the readout so you can lift the phone away from the sample |

## Layout

```
src/LensApp/
  Controls/CameraPreview.cs              cross-platform camera view (zoom, torch, ColorSampled)
  Handlers/CameraPreviewHandler.cs       shared handler + property mappers
  Platforms/Android/…Handler.Android.cs  CameraX backend, samples PreviewView.Bitmap
  Platforms/iOS/…Handler.iOS.cs          AVFoundation backend, samples the BGRA frames
  Models/ColorMath.cs                    sRGB ⇄ linear ⇄ Lab, CIEDE2000
  Models/PatchSampler.cs                 trimmed mean of the reticle patch
  Services/RalPalette.cs                 the RAL Classic table
  Services/RalMatcher.cs                 nearest-RAL search
  Services/WhiteBalanceService.cs        grey-card gains
  ViewModels/MainViewModel.cs            smoothing, formatting, commands
  MainPage.xaml                          the whole UI
```

## Build and run

Requires the .NET 9 SDK and the MAUI workload:

```bash
dotnet workload install maui
```

Android (from any OS):

```bash
dotnet build src/LensApp/LensApp.csproj -f net9.0-android -t:Run
```

iOS (macOS with Xcode only):

```bash
dotnet build src/LensApp/LensApp.csproj -f net9.0-ios -t:Run -p:RuntimeIdentifier=ios-arm64
```

Both targets need a **physical device** — neither simulator gives you a usable camera, and
the iOS simulator has no camera at all.

The CameraX packages are referenced with a floating `1.4.*` version so the newest available
binding is restored; pin them if you need reproducible restores.

## Accuracy — read this before trusting a number

A phone camera is not a spectrophotometer, and this app does not pretend otherwise:

* **The RAL hex values in `RalPalette.cs` are the commonly published sRGB approximations**, not
  measured spectral data. They identify a shade; they do not certify it. Swapping in your own
  measured values is a one-file change.
* **The camera ISP rewrites colour** — auto white balance, tone curve, saturation. The
  *Calibrate* button is what makes a reading meaningful: fill the reticle with a neutral white
  or grey reference under the same light as the sample, tap it, then measure. Recalibrate when
  the lighting changes.
* **Light the sample evenly.** The torch helps at close range but adds specular hotspots on
  glossy paint; hold the phone slightly off-axis. Metallic and pearlescent finishes shift with
  the viewing angle and will never resolve to one stable number.
* **ΔE is the honest part of the readout.** Under 2 is an excellent match, under 3.5 is a good
  one, and anything into double digits means the app found the nearest entry, not the right one.

## Known limits

* Portrait orientation only (both platforms are locked to it) — the reticle-to-sample mapping
  assumes it.
* Back camera only.
* No shot history or export beyond *Copy*, which puts the match and the measured hex on the
  clipboard.
