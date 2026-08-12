# Collapsible RAL Panel + Drag-to-Zoom Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the fixed RAL measurement panel into a draggable bottom sheet that starts closed
(camera full-screen behind it), and add a vertical drag-anywhere gesture for zoom alongside the
existing pinch and a simplified slider.

**Architecture:** `MainPage.xaml`'s root layout moves from two `Grid` rows (camera + panel) to a
single overlay `Grid` where the camera fills the whole page and the panel is a
`VerticalStackLayout` anchored to the bottom, positioned via `TranslationY` and dragged/tapped
through a handle. Zoom gains a `PanGestureRecognizer` alongside the existing pinch/double-tap
recognizers on the camera's transparent overlay `Grid`, writing to the same
`MainViewModel.ZoomFraction` the slider already uses. All new state (panel open/closed, drag
tracking) lives in `MainPage.xaml.cs` as plain fields — it's pure UI state, not view-model data.

**Tech Stack:** .NET MAUI (net10.0-android / net10.0-ios), C#, XAML. No new packages.

## Global Constraints

- Build and verify with `dotnet build src/LensApp/LensApp.csproj -f net10.0-android` after every
  task (from `CLAUDE.md`) — this repo has no automated test project, so "run the tests" in this
  plan means "build clean," and each task also lists a manual on-device checklist per
  `CLAUDE.md`'s physical-device requirement.
- Portrait orientation and back camera only, on both platforms — do not add any layout logic
  that assumes landscape (from `CLAUDE.md`).
- No changes to the colour pipeline (`Models/ColorMath.cs`, `Models/PatchSampler.cs`), RAL
  matching, or calibration logic (spec non-goal).
- No changes to the iOS platform handler — all changes are in shared XAML/code-behind, so iOS
  gets the same behaviour for free (spec non-goal).
- The panel always starts closed; no persistence of open/closed state across app restarts (spec
  non-goal).
- Work directly on `main`; commit after each task (from `CLAUDE.md`).

---

### Task 1: Simplify the zoom bar — drop the −/+ buttons

**Files:**
- Modify: `src/LensApp/MainPage.xaml:61-77`

**Interfaces:**
- Produces: the zoom `Border` gets `x:Name="ZoomBar"`, used by Task 6 to fade it out while the
  panel is open.

- [ ] **Step 1: Replace the zoom bar markup**

Replace:

```xml
                <!-- zoom -->
                <Border VerticalOptions="End" Margin="12"
                        BackgroundColor="{StaticResource Scrim}" Stroke="Transparent"
                        StrokeShape="RoundRectangle 22" Padding="8,4"
                        IsVisible="{Binding CanZoom}">
                    <Grid ColumnDefinitions="Auto,*,Auto" ColumnSpacing="4">
                        <Button Grid.Column="0" Text="−" FontSize="18" Padding="12,4"
                                BackgroundColor="Transparent"
                                Command="{Binding ZoomOutCommand}" />
                        <Slider Grid.Column="1"
                                Value="{Binding ZoomFraction, Mode=TwoWay}"
                                VerticalOptions="Center" />
                        <Button Grid.Column="2" Text="+" FontSize="18" Padding="12,4"
                                BackgroundColor="Transparent"
                                Command="{Binding ZoomInCommand}" />
                    </Grid>
                </Border>
```

With:

```xml
                <!-- zoom -->
                <Border x:Name="ZoomBar" VerticalOptions="End" Margin="12"
                        BackgroundColor="{StaticResource Scrim}" Stroke="Transparent"
                        StrokeShape="RoundRectangle 22" Padding="16,4"
                        IsVisible="{Binding CanZoom}">
                    <Slider WidthRequest="200"
                            Value="{Binding ZoomFraction, Mode=TwoWay}"
                            VerticalOptions="Center" />
                </Border>
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/LensApp/LensApp.csproj -f net10.0-android`
Expected: `Compilazione completata` / `Build succeeded`, 0 errors. (The `ZoomInCommand` /
`ZoomOutCommand` bindings are gone from XAML, but the properties still exist on the ViewModel
until Task 2 — no binding errors either way.)

- [ ] **Step 3: Manual check**

Run the app on a physical Android device (`dotnet build src/LensApp/LensApp.csproj -f
net10.0-android -t:Run`). Confirm the zoom bar shows only a slider (no `−`/`+` buttons) and
dragging the slider still zooms the preview.

- [ ] **Step 4: Commit**

```bash
git add src/LensApp/MainPage.xaml
git commit -m "Simplify zoom bar to a single slider"
```

---

### Task 2: Remove the now-unused zoom commands

**Files:**
- Modify: `src/LensApp/ViewModels/MainViewModel.cs:31-32` (constructor wiring)
- Modify: `src/LensApp/ViewModels/MainViewModel.cs:203-204` (property declarations)

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing new — this is dead-code removal following Task 1, which was `ZoomInCommand`
  / `ZoomOutCommand`'s only caller.

- [ ] **Step 1: Confirm nothing else references the commands**

Run: `grep -rn "ZoomInCommand\|ZoomOutCommand" src/LensApp`
Expected: no matches (Task 1 already removed the only XAML bindings).

- [ ] **Step 2: Remove the constructor wiring**

In `MainViewModel`'s constructor, delete these two lines:

```csharp
        ZoomInCommand = new Command(() => Zoom = Math.Min(MaxZoom, Zoom * 1.5));
        ZoomOutCommand = new Command(() => Zoom = Math.Max(1.0, Zoom / 1.5));
```

- [ ] **Step 3: Remove the property declarations**

Delete these two lines from the `// ---- commands ----` section:

```csharp
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src/LensApp/LensApp.csproj -f net10.0-android`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/LensApp/ViewModels/MainViewModel.cs
git commit -m "Remove ZoomInCommand/ZoomOutCommand, unused since the zoom bar simplification"
```

---

### Task 3: Zoom by dragging anywhere on the camera preview

**Files:**
- Modify: `src/LensApp/MainPage.xaml:26-30` (add a `PanGestureRecognizer`)
- Modify: `src/LensApp/MainPage.xaml.cs` (add the handler)

**Interfaces:**
- Consumes: `MainViewModel.ZoomFraction` (`double`, get/set, clamped 0..1 internally) — already
  exists.
- Produces: nothing new for later tasks.

- [ ] **Step 1: Add the gesture recognizer**

Replace:

```xml
            <Grid BackgroundColor="Transparent">
                <Grid.GestureRecognizers>
                    <PinchGestureRecognizer PinchUpdated="OnPinchUpdated" />
                    <TapGestureRecognizer NumberOfTapsRequired="2" Tapped="OnDoubleTapped" />
                </Grid.GestureRecognizers>
```

With:

```xml
            <Grid BackgroundColor="Transparent">
                <Grid.GestureRecognizers>
                    <PinchGestureRecognizer PinchUpdated="OnPinchUpdated" />
                    <PanGestureRecognizer PanUpdated="OnZoomPanUpdated" />
                    <TapGestureRecognizer NumberOfTapsRequired="2" Tapped="OnDoubleTapped" />
                </Grid.GestureRecognizers>
```

- [ ] **Step 2: Add the handler**

In `MainPage.xaml.cs`, add this field and method to the class (after `OnDoubleTapped` is fine):

```csharp
    double _zoomPanStartFraction;

    void OnZoomPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        // The full 0..1 zoom range is covered by ~280 units of drag, regardless of screen
        // height, so the gesture feels the same on every device.
        const double DragUnitsForFullRange = 280;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _zoomPanStartFraction = _vm.ZoomFraction;
                break;
            case GestureStatus.Running:
                var delta = -e.TotalY / DragUnitsForFullRange;
                _vm.ZoomFraction = Math.Clamp(_zoomPanStartFraction + delta, 0.0, 1.0);
                break;
        }
    }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/LensApp/LensApp.csproj -f net10.0-android`
Expected: 0 errors.

- [ ] **Step 4: Manual check**

On a physical device: drag a finger up on the live preview — it should zoom in; drag down — zoom
out. Confirm pinch-to-zoom and double-tap-to-reset still work afterward (the three gestures
share the same `Zoom`/`ZoomFraction` property, so they should never fight — a pinch followed by
a drag should continue from wherever the pinch left off).

- [ ] **Step 5: Commit**

```bash
git add src/LensApp/MainPage.xaml src/LensApp/MainPage.xaml.cs
git commit -m "Add vertical drag-to-zoom on the camera preview"
```

---

### Task 4: Restructure the layout — full-screen camera + static bottom sheet

**Files:**
- Modify: `src/LensApp/MainPage.xaml` (full-file rewrite)

**Interfaces:**
- Produces: `x:Name`s consumed by Task 5 and Task 6 — `PanelContainer`
  (`VerticalStackLayout`), `PanelHandle` (`Grid`), `PanelCard` (`Border`). `ZoomBar` (from
  Task 1) is unchanged by this task.

This task only changes structure — the panel has no drag/tap behaviour yet and renders
permanently "open" (flush with the bottom, camera visible full-screen behind and above it). That
is intentional and independently testable; Task 5 adds the interactivity.

- [ ] **Step 1: Rewrite `MainPage.xaml`**

Write the complete file:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:controls="clr-namespace:LensApp.Controls"
             xmlns:models="clr-namespace:LensApp.Models"
             xmlns:vm="clr-namespace:LensApp.ViewModels"
             x:Class="LensApp.MainPage"
             x:DataType="vm:MainViewModel"
             Title="LensApp"
             BackgroundColor="{StaticResource Background}">

    <Grid>

        <!-- ============================ live camera (full screen) ============================ -->
        <Grid>

            <controls:CameraPreview x:Name="Camera"
                                    Zoom="{Binding Zoom}"
                                    IsTorchOn="{Binding IsTorchOn}"
                                    SampleSize="0.07"
                                    SampleRate="8"
                                    ColorSampled="OnColorSampled"
                                    CameraError="OnCameraError" />

            <!-- overlay: reticle, chips and the zoom control -->
            <Grid BackgroundColor="Transparent">
                <Grid.GestureRecognizers>
                    <PinchGestureRecognizer PinchUpdated="OnPinchUpdated" />
                    <PanGestureRecognizer PanUpdated="OnZoomPanUpdated" />
                    <TapGestureRecognizer NumberOfTapsRequired="2" Tapped="OnDoubleTapped" />
                </Grid.GestureRecognizers>

                <!-- reticle: what gets measured -->
                <Grid x:Name="Reticle"
                      HorizontalOptions="Center"
                      VerticalOptions="Center"
                      WidthRequest="64"
                      HeightRequest="64"
                      InputTransparent="True">
                    <Border Stroke="#000000" StrokeThickness="4" BackgroundColor="Transparent"
                            StrokeShape="RoundRectangle 10" Opacity="0.55" />
                    <Border Stroke="#FFFFFF" StrokeThickness="2" BackgroundColor="Transparent"
                            StrokeShape="RoundRectangle 10" />
                </Grid>

                <!-- top chips -->
                <HorizontalStackLayout VerticalOptions="Start" HorizontalOptions="Center"
                                       Margin="12" Spacing="8">
                    <Button Style="{StaticResource OverlayButton}"
                            Text="{Binding TorchLabel}"
                            Command="{Binding ToggleTorchCommand}" />
                    <Button Style="{StaticResource OverlayButton}"
                            Text="{Binding FreezeLabel}"
                            Command="{Binding ToggleFreezeCommand}" />
                    <Border BackgroundColor="{StaticResource Scrim}" Stroke="Transparent"
                            StrokeShape="RoundRectangle 20" Padding="12,8"
                            VerticalOptions="Center">
                        <Label Text="{Binding ZoomText}" FontAttributes="Bold" />
                    </Border>
                </HorizontalStackLayout>

                <!-- zoom -->
                <Border x:Name="ZoomBar" VerticalOptions="End" Margin="12"
                        BackgroundColor="{StaticResource Scrim}" Stroke="Transparent"
                        StrokeShape="RoundRectangle 22" Padding="16,4"
                        IsVisible="{Binding CanZoom}">
                    <Slider WidthRequest="200"
                            Value="{Binding ZoomFraction, Mode=TwoWay}"
                            VerticalOptions="Center" />
                </Border>
            </Grid>
        </Grid>

        <!-- ============================ RAL panel (draggable bottom sheet) ============================ -->
        <VerticalStackLayout x:Name="PanelContainer" VerticalOptions="End" Spacing="0">

            <!-- drag handle -->
            <Grid x:Name="PanelHandle" HeightRequest="28" BackgroundColor="{StaticResource Scrim}">
                <Border WidthRequest="40" HeightRequest="5" HorizontalOptions="Center" VerticalOptions="Center"
                        BackgroundColor="{StaticResource Outline}" Stroke="Transparent"
                        StrokeShape="RoundRectangle 3" />
            </Grid>

            <!-- ============================ measurement ============================ -->
            <Border x:Name="PanelCard" Style="{StaticResource Card}" Margin="10,0,10,10">
                <VerticalStackLayout Spacing="12">

                    <Grid ColumnDefinitions="Auto,*" ColumnSpacing="14">

                        <!-- measured colour -->
                        <Border Grid.Column="0" WidthRequest="92" HeightRequest="92"
                                BackgroundColor="{Binding MeasuredColor}"
                                Stroke="{StaticResource Outline}" StrokeThickness="1"
                                StrokeShape="RoundRectangle 12" Padding="0">
                            <Label Text="{Binding HexText}"
                                   TextColor="{Binding MeasuredForeground}"
                                   FontSize="13" FontAttributes="Bold"
                                   HorizontalOptions="Center" VerticalOptions="End"
                                   Margin="0,0,0,8" />
                        </Border>

                        <!-- best RAL match -->
                        <VerticalStackLayout Grid.Column="1" Spacing="2" VerticalOptions="Center">
                            <Label Text="{Binding BestMatch.Code, FallbackValue='—'}"
                                   FontSize="24" FontAttributes="Bold" />
                            <Label Text="{Binding BestMatch.Name, FallbackValue='point the reticle at a surface'}"
                                   FontSize="15" TextColor="{StaticResource Accent}" />
                            <Label FontSize="12" TextColor="{StaticResource TextSecondary}">
                                <Label.FormattedText>
                                    <FormattedString>
                                        <Span Text="{Binding BestMatch.DeltaEText, FallbackValue=''}" />
                                        <Span Text="  ·  " />
                                        <Span Text="{Binding BestMatch.Quality, FallbackValue=''}" />
                                    </FormattedString>
                                </Label.FormattedText>
                            </Label>
                            <Label Text="{Binding RgbText}" Style="{StaticResource Caption}" />
                            <Label Text="{Binding LabText}" Style="{StaticResource Caption}" />
                        </VerticalStackLayout>
                    </Grid>

                    <!-- runners-up -->
                    <ScrollView Orientation="Horizontal" HorizontalScrollBarVisibility="Never">
                        <HorizontalStackLayout Spacing="8"
                                               BindableLayout.ItemsSource="{Binding Alternatives}">
                            <BindableLayout.ItemTemplate>
                                <DataTemplate x:DataType="models:RalMatch">
                                    <Border BackgroundColor="{StaticResource SurfaceRaised}"
                                            Stroke="{StaticResource Outline}" StrokeThickness="1"
                                            StrokeShape="RoundRectangle 10" Padding="8,6">
                                        <HorizontalStackLayout Spacing="8">
                                            <Border WidthRequest="26" HeightRequest="26"
                                                    BackgroundColor="{Binding Swatch}"
                                                    Stroke="Transparent"
                                                    StrokeShape="RoundRectangle 6" />
                                            <VerticalStackLayout Spacing="0" VerticalOptions="Center">
                                                <Label Text="{Binding Code}" FontSize="12" FontAttributes="Bold" />
                                                <Label Text="{Binding DeltaEText}" FontSize="10"
                                                       TextColor="{StaticResource TextSecondary}" />
                                            </VerticalStackLayout>
                                        </HorizontalStackLayout>
                                    </Border>
                                </DataTemplate>
                            </BindableLayout.ItemTemplate>
                        </HorizontalStackLayout>
                    </ScrollView>

                    <!-- actions -->
                    <Grid ColumnDefinitions="*,*,*" ColumnSpacing="8">
                        <Button Grid.Column="0" Text="Calibrate" Command="{Binding CalibrateCommand}" />
                        <Button Grid.Column="1" Text="Reset WB"
                                Command="{Binding ResetCalibrationCommand}"
                                IsEnabled="{Binding IsCalibrated}" />
                        <Button Grid.Column="2" Text="Copy" Command="{Binding CopyCommand}" />
                    </Grid>

                    <Grid ColumnDefinitions="Auto,*" ColumnSpacing="8">
                        <Border Grid.Column="0" BackgroundColor="{StaticResource AccentMuted}"
                                Stroke="Transparent" StrokeShape="RoundRectangle 8"
                                Padding="8,3" VerticalOptions="Start">
                            <Label Text="{Binding CalibrationLabel}" FontSize="11"
                                   TextColor="{StaticResource Accent}" />
                        </Border>
                        <Label Grid.Column="1" Text="{Binding Status}"
                               Style="{StaticResource Caption}" VerticalOptions="Center" />
                    </Grid>
                </VerticalStackLayout>
            </Border>
        </VerticalStackLayout>
    </Grid>
</ContentPage>
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/LensApp/LensApp.csproj -f net10.0-android`
Expected: 0 errors.

- [ ] **Step 3: Manual check**

On a physical device: the camera preview now fills the entire screen. The RAL panel sits at the
bottom with a small grip bar above the card — it does not yet drag or collapse (that's Task 5),
but all its content (measured colour, match, runners-up, buttons) should look and work exactly
as before.

- [ ] **Step 4: Commit**

```bash
git add src/LensApp/MainPage.xaml
git commit -m "Make the camera preview full-screen; RAL panel becomes a bottom overlay"
```

---

### Task 5: Make the panel draggable and collapsible, starting closed

**Files:**
- Modify: `src/LensApp/MainPage.xaml` (3 small edits)
- Modify: `src/LensApp/MainPage.xaml.cs` (new fields + 4 new methods)

**Interfaces:**
- Consumes: `PanelContainer`, `PanelHandle`, `PanelCard` (from Task 4).
- Produces: `SetPanelOpen(bool open)` — consumed by Task 6 to also fade `ZoomBar`.

- [ ] **Step 1: Make the panel start invisible until it's positioned**

In `MainPage.xaml`, replace:

```xml
        <VerticalStackLayout x:Name="PanelContainer" VerticalOptions="End" Spacing="0">
```

With:

```xml
        <VerticalStackLayout x:Name="PanelContainer" VerticalOptions="End" Spacing="0" Opacity="0">
```

(Without this, the panel would render at `TranslationY="0"` — i.e. visually open — for one
frame before the code below measures the card and snaps it closed.)

- [ ] **Step 2: Wire up the handle's gestures**

Replace:

```xml
            <Grid x:Name="PanelHandle" HeightRequest="28" BackgroundColor="{StaticResource Scrim}">
                <Border WidthRequest="40" HeightRequest="5" HorizontalOptions="Center" VerticalOptions="Center"
                        BackgroundColor="{StaticResource Outline}" Stroke="Transparent"
                        StrokeShape="RoundRectangle 3" />
            </Grid>
```

With:

```xml
            <Grid x:Name="PanelHandle" HeightRequest="28" BackgroundColor="{StaticResource Scrim}">
                <Grid.GestureRecognizers>
                    <PanGestureRecognizer PanUpdated="OnPanelHandlePanUpdated" />
                    <TapGestureRecognizer Tapped="OnPanelHandleTapped" />
                </Grid.GestureRecognizers>
                <Border WidthRequest="40" HeightRequest="5" HorizontalOptions="Center" VerticalOptions="Center"
                        BackgroundColor="{StaticResource Outline}" Stroke="Transparent"
                        StrokeShape="RoundRectangle 3" />
            </Grid>
```

- [ ] **Step 3: Wire up the card's `SizeChanged`**

Replace:

```xml
            <Border x:Name="PanelCard" Style="{StaticResource Card}" Margin="10,0,10,10">
```

With:

```xml
            <Border x:Name="PanelCard" Style="{StaticResource Card}" Margin="10,0,10,10"
                    SizeChanged="OnPanelCardSizeChanged">
```

- [ ] **Step 4: Add the panel state machine**

In `MainPage.xaml.cs`, add these fields and methods to the class:

```csharp
    bool _panelOpen;
    bool _panelPositioned;
    double _panelClosedOffset;
    double _panelDragStartTranslationY;

    void OnPanelCardSizeChanged(object? sender, EventArgs e)
    {
        if (PanelCard.Height <= 0) return;

        _panelClosedOffset = PanelCard.Height;
        if (!_panelOpen) PanelContainer.TranslationY = _panelClosedOffset;

        if (_panelPositioned) return;
        _panelPositioned = true;
        PanelContainer.Opacity = 1;
    }

    void OnPanelHandlePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panelDragStartTranslationY = PanelContainer.TranslationY;
                break;
            case GestureStatus.Running:
                PanelContainer.TranslationY = Math.Clamp(
                    _panelDragStartTranslationY + e.TotalY, 0, _panelClosedOffset);
                break;
            case GestureStatus.Completed:
                SetPanelOpen(PanelContainer.TranslationY < _panelClosedOffset / 2);
                break;
        }
    }

    void OnPanelHandleTapped(object? sender, TappedEventArgs e) => SetPanelOpen(!_panelOpen);

    void SetPanelOpen(bool open)
    {
        _panelOpen = open;
        PanelContainer.TranslateTo(0, open ? 0 : _panelClosedOffset, 200, Easing.CubicOut);
    }
```

`_panelOpen` defaults to `false` (the C# default for `bool`), which is exactly the "starts
closed" requirement — no explicit initialization needed.

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build src/LensApp/LensApp.csproj -f net10.0-android`
Expected: 0 errors.

- [ ] **Step 6: Manual check**

On a physical device:
- On launch, only the handle grip is visible at the bottom; the camera fills the rest of the
  screen, with no visible flash of the panel being open first.
- Dragging the handle up reveals the panel; releasing past the midpoint snaps it fully open,
  releasing before the midpoint snaps it back closed.
- Dragging the handle down from open closes it the same way.
- Tapping the handle toggles open/closed with an animation.
- Rotate-independent check: trigger a status message that wraps to two lines (e.g. deny camera
  permission once to see a longer `Status` string) and confirm the closed handle position still
  sits flush with the screen bottom afterward.

- [ ] **Step 7: Commit**

```bash
git add src/LensApp/MainPage.xaml src/LensApp/MainPage.xaml.cs
git commit -m "Make the RAL panel a draggable, tappable bottom sheet that starts closed"
```

---

### Task 6: Hide the zoom bar while the panel is open

**Files:**
- Modify: `src/LensApp/MainPage.xaml.cs:SetPanelOpen`

**Interfaces:**
- Consumes: `ZoomBar` (from Task 1), `SetPanelOpen` (from Task 5).

- [ ] **Step 1: Extend `SetPanelOpen`**

Replace:

```csharp
    void SetPanelOpen(bool open)
    {
        _panelOpen = open;
        PanelContainer.TranslateTo(0, open ? 0 : _panelClosedOffset, 200, Easing.CubicOut);
    }
```

With:

```csharp
    void SetPanelOpen(bool open)
    {
        _panelOpen = open;
        ZoomBar.FadeTo(open ? 0 : 1, 150);
        ZoomBar.InputTransparent = open;
        PanelContainer.TranslateTo(0, open ? 0 : _panelClosedOffset, 200, Easing.CubicOut);
    }
```

(`ZoomBar`'s `IsVisible` stays bound to `CanZoom` — this only adds a second, independent gate on
`Opacity`, so a `MaxZoom` change while the panel is open can't accidentally pop the bar back
into view.)

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/LensApp/LensApp.csproj -f net10.0-android`
Expected: 0 errors.

- [ ] **Step 3: Manual check**

On a physical device: open the RAL panel (drag or tap) and confirm the zoom bar fades out and
stops responding to touch; close the panel and confirm it fades back in and works again. Confirm
drag-to-zoom and pinch-to-zoom still work while the panel is open (only the visible slider bar
should be affected).

- [ ] **Step 4: Commit**

```bash
git add src/LensApp/MainPage.xaml.cs
git commit -m "Fade out the zoom bar while the RAL panel is open"
```

---

### Task 7: Update the README to match the new interaction model

**Files:**
- Modify: `README.md`

**Interfaces:** none (documentation only).

- [ ] **Step 1: Update the mockup and the zoom feature row**

Replace the ASCII mockup:

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

With:

```
┌──────────────────────────────┐
│  Torch off  │ Hold │  2.4×   │   full-screen live preview
│                              │
│            ┌──┐              │   drag up/down anywhere to zoom — pinch and
│            └──┘              │   the slider work too
│                    ══════    │
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

In the "What it does" table, replace the Zoom row:

```
| Zoom | Real camera zoom (`CameraControl.SetZoomRatio` / `AVCaptureDevice.VideoZoomFactor`), driven by pinch, a slider, ± buttons, or a double tap |
```

With:

```
| Zoom | Real camera zoom (`CameraControl.SetZoomRatio` / `AVCaptureDevice.VideoZoomFactor`), driven by pinch, a vertical drag anywhere on the preview, a slider, or a double tap |
```

Add a new row right after it:

```
| RAL panel | Starts collapsed to a thin handle so the preview gets the full screen; drag the handle up or tap it to reveal the match, drag down or tap again to hide it |
```

- [ ] **Step 2: Proofread**

Read the updated `README.md` section and confirm the table renders sensibly and the mockup lines
up (monospace, consistent column widths) — this is a docs-only change with no build step.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "Document the collapsible RAL panel and drag-to-zoom in the README"
```
