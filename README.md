# LensApp

A .NET MAUI app for Android and iOS that points the camera at a surface, zooms in on it, reads
the colour inside the reticle in real time, and tells you which **RAL Classic** shade it is.
The torch can be switched on to light the sample.

```
┌──────────────────────────────┐
│  Torch off  │ Hold │  2.4×   │   full-screen live preview
│                              │
│                             ║│   drag anywhere to zoom - pinch, double-tap
│            ┌──┐             ║│   and the vertical slider on the right all
│            └──┘             ║│   work too
│                              │
│              ▔▔▔▔            │   ← drag up, or tap, to reveal the RAL panel
└──────────────────────────────┘
        (dragged open)
┌──────────────────────────────┐
│              ▔▔▔▔            │   ← drag down, or tap, to hide it again
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
| Zoom | Real camera zoom (`CameraControl.SetZoomRatio` / `AVCaptureDevice.VideoZoomFactor`), driven by pinch, drag, the vertical slider, or a double tap |
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

Requires the .NET 10 SDK (LTS) and the MAUI workload:

```bash
dotnet workload install maui
```

Android (from any OS):

```bash
dotnet build src/LensApp/LensApp.csproj -f net10.0-android -t:Run
```

iOS (macOS with Xcode only):

```bash
dotnet build src/LensApp/LensApp.csproj -f net10.0-ios -t:Run -p:RuntimeIdentifier=ios-arm64
```

Both targets need a **physical device** — neither simulator gives you a usable camera, and
the iOS simulator has no camera at all.

The CameraX packages are pinned to `1.6.1.1` for reproducible restores.


## Release builds

Release enables R8 code and resource shrinking, a full trim and IL stripping after AOT — .NET
Android leaves all of these off by default, and they roughly halve the dex that Play ships to
every device regardless of ABI. Two consequences worth remembering: framework exception
messages collapse to resource ids (`UseSystemResourceKeys`), and the same trim settings apply
to the iOS target, which has not been tested.

Signing uses a **Play upload key** — Google holds the app signing key itself under Play App
Signing. Create the key once, outside the repo:

```bash
keytool -genkeypair -v -keystore "%USERPROFILE%\keys\lensapp-upload.jks" \
  -alias lensapp-upload -keyalg RSA -keysize 4096 -validity 10000
```

Back it up somewhere durable; losing it means a Play support reset. Point the build at it
through the environment, so no secret is ever committed or typed on a command line:

```bash
setx LENSAPP_KEYSTORE      "%USERPROFILE%\keys\lensapp-upload.jks"
setx LENSAPP_KEYSTORE_PASS "..."
setx LENSAPP_KEY_PASS      "..."
```

Then, after bumping `ApplicationVersion` in `LensApp.csproj` (Play needs a unique, increasing
versionCode per upload):

```bash
dotnet publish src/LensApp/LensApp.csproj -f net10.0-android -c Release
```

Upload `bin/Release/net10.0-android/publish/com.qubitstudio.lensapp-Signed.aab`, and attach
`bin/Release/net10.0-android/mapping.txt` to the same release so R8-shrunk crash reports
deobfuscate. Building Release without the environment variables fails with `LENS001` rather
than handing back a debug-signed bundle that Play would reject.

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
