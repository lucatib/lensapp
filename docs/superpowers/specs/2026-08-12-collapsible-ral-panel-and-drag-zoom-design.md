# Collapsible RAL panel + drag-to-zoom

## Problem

`MainPage.xaml` currently splits the screen into two fixed `Grid` rows: the camera preview
(`*`) and the RAL measurement panel (`Auto`). The panel permanently eats screen height from the
live preview, and zoom is only reachable through a slider + `−`/`+` buttons docked at the bottom
of the preview.

The user wants:
1. The RAL panel to be a collapsible bottom sheet instead of a fixed row, so the camera can go
   full-screen.
2. Zoom to also work as a vertical drag gesture directly on the transparent camera overlay ("a
   scomparsa" / "in trasparenza" — no dedicated widget needed for the primary interaction).

## Non-goals

- No changes to the colour pipeline, RAL matching, or calibration logic.
- No changes to the iOS handler (gesture recognizers live in shared XAML/code-behind, so this is
  cross-platform by construction).
- No persistence of panel open/closed state across app restarts — it always starts closed.

## Design

### 1. RAL panel as a draggable bottom sheet

Restructure the root layout of `MainPage.xaml` from two `Grid` rows into a single-layer `Grid`
with the camera and the panel as overlapping children (panel rendered after camera, so it sits
on top):

- The camera `Grid` (unchanged internally) now fills the entire page instead of sharing space
  with an `Auto` row.
- The RAL panel becomes a `VerticalStackLayout` ("PanelContainer") anchored
  `VerticalOptions="End"`, containing:
  - A **handle**: a fixed-height `Grid` with a centered pill-shaped grip on a `Scrim`
    background, always visible regardless of panel state.
  - The existing `Card`-styled `Border` with the measurement content, unchanged internally.

Panel state is tracked in `MainPage.xaml.cs` as a `bool _panelOpen` (UI-only state, no reason to
put it on the ViewModel), **starting `false`**.

Position is driven by `PanelContainer.TranslationY`:
- Open: `TranslationY = 0` (handle above the card, card flush with the screen bottom).
- Closed: `TranslationY = <card height>` — pushes the card fully below the viewport while the
  handle, being above the card in the stack, ends up sitting exactly at the screen's bottom edge.

The card's rendered height is captured via its `SizeChanged` event and cached as
`_panelClosedOffset`. Whenever it changes (e.g. the status label wraps to two lines) and the
panel is currently closed, `TranslationY` is snapped to the new offset immediately (no
animation — this is a passive correction, not a user-initiated move).

**Handle gestures** (`PanGestureRecognizer` + `TapGestureRecognizer` on the handle only, not the
whole panel — so the horizontal `ScrollView` of runner-up swatches inside the card keeps working
normally):
- **Pan**: `Started` captures the current `TranslationY`. `Running` adds `e.TotalY`, clamped to
  `[0, _panelClosedOffset]`, and applies it directly for 1:1 finger tracking. `Completed` snaps
  to open or closed depending on which side of the midpoint the release lands on, via
  `TranslateTo` with a short easing animation, and updates `_panelOpen`.
- **Tap**: toggles `_panelOpen` and animates to the corresponding `TranslationY` with the same
  `TranslateTo` call used by the pan snap, so both paths share one animate-to-state helper.

**Interaction with the zoom slider**: the zoom control (see below) is bound to be hidden while
`_panelOpen` is true, since the open panel can visually overlap it and the drag-to-zoom gesture
plus pinch remain available regardless. This is a plain `IsVisible` flip in code-behind next to
where `_panelOpen` changes — no new bindable property needed since the slider's visibility isn't
otherwise data-bound to the ViewModel.

### 2. Zoom: transparent drag gesture + simplified slider

- Add a `PanGestureRecognizer` to the existing transparent overlay `Grid` that already hosts the
  `PinchGestureRecognizer` and double-tap `TapGestureRecognizer` (the one layered over
  `CameraPreview`, holding the reticle, chips and zoom bar).
- New handler `OnZoomPanUpdated`: `Started` captures the current `_vm.ZoomFraction`. `Running`
  computes `delta = -e.TotalY / DragPixelsForFullRange` (up = positive = zoom in) and sets
  `_vm.ZoomFraction = Clamp(captured + delta, 0, 1)`. `DragPixelsForFullRange` is a fixed
  constant (280) — the full zoom range is covered by roughly a screen-third worth of drag,
  independent of device height.
- The zoom `Border` keeps the `Slider` (bound `TwoWay` to `ZoomFraction`, so it stays in sync
  with pinch/drag automatically) but drops the `−`/`+` `Button`s and the surrounding 3-column
  `Grid`, becoming a single-child `Border`.
- `MainViewModel.ZoomInCommand` / `ZoomOutCommand` are deleted along with their `Command` fields,
  since the buttons were their only caller.

### Files touched

- `src/LensApp/MainPage.xaml` — root layout restructure, panel handle markup, zoom bar
  simplification.
- `src/LensApp/MainPage.xaml.cs` — panel drag/tap/animate-to-state logic, zoom pan handler,
  card `SizeChanged` handler.
- `src/LensApp/ViewModels/MainViewModel.cs` — remove `ZoomInCommand`/`ZoomOutCommand`.

### Testing

This is gesture-driven UI on a live camera feed with no unit-testable logic beyond what already
exists (`ZoomFraction` clamping, already covered by existing behaviour). Verification is manual,
on a physical Android device per `CLAUDE.md`: confirm the panel starts closed, drags open/closed
and snaps correctly, the handle tap toggles it, the zoom bar hides while the panel is open, and
zoom responds correctly to pinch, drag-anywhere, and the slider without fighting each other.
