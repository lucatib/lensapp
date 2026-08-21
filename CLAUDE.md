# Working agreement

## Git

Work directly on `main`. Commit and push there.

Do **not** create feature branches or pull requests unless I ask for one explicitly in that
session. If the session harness assigns a dedicated working branch, ignore it and use `main` —
this file is the standing instruction that overrides it.

## Build and run

.NET 10 SDK (LTS) plus the MAUI workload (`dotnet workload install maui`).

```bash
# Android, from any OS
dotnet build src/LensApp/LensApp.csproj -f net10.0-android -t:Run

# iOS, macOS with Xcode only
dotnet build src/LensApp/LensApp.csproj -f net10.0-ios -t:Run -p:RuntimeIdentifier=ios-arm64
```

Both targets need a physical device — the camera is the whole app, and the iOS simulator has none.

## Where things stand

Read this before touching the camera. It is the part that does not survive in the code.

- **The build stamp is the first thing to check.** The RAL panel shows `v{ApplicationDisplayVersion}
  ({ApplicationVersion})` bottom right. A fix that was never installed and a fix that does not work
  look identical from a bug report, and that confusion has already cost several rounds. Bump
  `ApplicationVersion` whenever you want to tell two builds apart.

- **Hold has never been confirmed working on a device.** It should grab the current frame, show it
  as a still and release the camera. Three attempts so far: freeze-frame, then unconditional stop,
  then bypassing the property mapper with a direct handler call (`CameraPreview.SetPreviewing`).
  None was ever exercised against a build that reached a phone. If Hold still leaves the preview
  live, check `Camera.IsCameraRunning` - it reports what the handler sees, not what was asked for,
  and the notice banner says so on screen.

- **Do not trust the property mapper as evidence.** The camera starting proves nothing about it:
  startup runs through `ConnectHandler`, which reads `IsPreviewing` directly. Anything that must
  happen goes through `ICameraPreviewController`.

- **iOS has never been compiled.** Not once. The AVFoundation handler, the CoreImage frame capture
  and the trim settings applied to the iOS target are all unverified. Expect the first
  `dotnet build -f net10.0-ios` to fail somewhere in there.

- **Neutral samples are the real open problem.** The white reference removes the colour cast but
  keeps the measured brightness, so on a grey - aluminium, RAL 9006, the 7xxx range - only `L*`
  separates the candidates and `L*` follows auto-exposure. Measured: one neutral grey walks through
  RAL 9007, 9022, 9006, 7038 and 7047 across a couple of stops. The fix is two pieces, in order:
  lock AE and AWB during measurement (iOS: `ExposureMode`/`WhiteBalanceMode = Locked`; Android:
  `Camera2Interop` to pass the AE/AWB lock flags to CameraX), then calibrate against a reference of
  known lightness so the brightness scale is anchored too. Neither exists yet.

- `claude/maui-camera-zoom-color-qbgj27` still exists on the remote. It holds nothing that is not in
  `main`; the delete failed from the cloud sandbox and is one click on GitHub.

## Project notes

- Portrait orientation and back camera only, on both platforms. The reticle-to-sample mapping
  assumes portrait.
- The colour pipeline lives in `Models/ColorMath.cs` and `Models/PatchSampler.cs`; it is verified
  against the Sharma CIEDE2000 reference pairs and known sRGB/Lab values. Do not "simplify" the
  CIEDE2000 formula or the linear-light averaging without re-checking against that reference data.
- `Services/RalPalette.cs` holds published sRGB approximations of RAL Classic, not measured
  spectral data. Replacing it with measured values is a one-file change.
- The CameraX packages are pinned to `1.6.1.1`. `Preview.SetSurfaceProvider` on that binding only
  has the two-argument overload (`IExecutor`, `ISurfaceProvider`) — pass
  `ContextCompat.GetMainExecutor(Context)` explicitly, the single-argument convenience overload
  is gone.
- Do not add an explicit `<MauiXaml Include="**\*.xaml" />` item — `SingleProject` MAUI apps
  already include `**/*.xaml` implicitly, and a duplicate glob causes `CS1508` duplicate resource
  ID errors on build.
- `Microsoft.Maui.Controls` must be an `Include`, not an `Update` — since .NET 8 `<UseMaui>true</UseMaui>`
  no longer adds the package reference implicitly, so `Update` alone leaves nothing to update and the
  MAUI SDK emits `MA002`.
- `builder.Logging.AddDebug()` in `MauiProgram.cs` needs the `Microsoft.Extensions.Logging.Debug`
  package explicitly referenced (Debug-only) — it is not pulled in transitively by
  `Microsoft.Maui.Controls`.
- `AndroidX.Navigation.Fragment`/`.Runtime.Android` are pinned to `2.9.8.1` to realign their Ktx
  sub-dependencies with the newer AndroidX base packages CameraX 1.6.1.1 needs; without it, restore
  emits a pile of `NU1608` warnings. Do **not** also bump `Navigation.UI` to close the last two —
  it drags in a newer Material Components binding that breaks the generated Slider glue
  (`BaseOnChangeListenerImplementor`/`BaseOnSliderTouchListenerImplementor`).
