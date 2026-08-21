using LensApp.Controls;
using LensApp.ViewModels;

namespace LensApp;

public partial class MainPage : ContentPage
{
    readonly MainViewModel _vm;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();

        _vm = viewModel;
        BindingContext = _vm;

        // MaxZoom / IsTorchAvailable are discovered by the handler once the camera opens.
        Camera.PropertyChanged += OnCameraPropertyChanged;
        Camera.SizeChanged += OnCameraSizeChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Coming back to a held reading must not restart the camera behind the frozen still.
        if (_vm.IsFrozen) return;

        if (await EnsureCameraPermissionAsync()) Camera.SetPreviewing(true);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Releases the camera (and the torch) whenever the page is not on screen.
        Camera.SetPreviewing(false);
    }

    async Task<bool> EnsureCameraPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.Camera>();

        if (status == PermissionStatus.Granted) return true;

        _vm.Status = "Camera permission denied - grant it in the system settings to measure colours.";
        return false;
    }

    void OnCameraPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == CameraPreview.MaxZoomProperty.PropertyName)
            _vm.MaxZoom = Camera.MaxZoom;
        else if (e.PropertyName == CameraPreview.IsTorchAvailableProperty.PropertyName)
            _vm.IsTorchAvailable = Camera.IsTorchAvailable;
    }

    void OnCameraSizeChanged(object? sender, EventArgs e)
    {
        // Keep the drawn reticle the same size as the patch the handler actually samples.
        var shortest = Math.Min(Camera.Width, Camera.Height);
        if (shortest <= 0) return;

        var side = Math.Max(44, shortest * Camera.SampleSize);
        Reticle.WidthRequest = side;
        Reticle.HeightRequest = side;
    }

    void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsFrozen)) _ = ApplyFreezeAsync();
        else if (e.PropertyName == nameof(MainViewModel.Zoom)) ApplyFrozenZoom();
    }

    /// <summary>
    /// Hold shows the current frame as a still and releases the camera; Resume puts the live
    /// preview back.
    ///
    /// The camera is stopped whether or not the still could be grabbed. Bailing out on a failed
    /// grab left the preview running, which is the one outcome the button must never produce:
    /// it says Hold and the image keeps moving, and the explanation lands in the status line
    /// inside a panel that is closed by default.
    /// </summary>
    async Task ApplyFreezeAsync()
    {
        if (_vm.IsFrozen)
        {
            // Grab before stopping - there is no frame to take afterwards.
            var frame = await Camera.CaptureFrameAsync();

            if (frame is not null)
            {
                FrozenFrame.Source = frame;
                FrozenFrame.IsVisible = true;
                _vm.Notice = string.Empty;

                // The still froze the framing along with the colour, so the zoom range now
                // starts where the capture did - see ApplyFrozenZoom.
                _frozenZoom = _vm.Zoom;
                _vm.MinZoom = _frozenZoom;
                ApplyFrozenZoom();
            }
            else
            {
                _vm.Notice = "Held, but the still could not be grabbed.";
            }

            Camera.SetPreviewing(false);

            // Report what actually happened rather than what was asked for: a preview that keeps
            // running under a button that says Hold is the whole complaint.
            if (Camera.IsCameraRunning)
                _vm.Notice = "Hold did not release the camera.";
        }
        else
        {
            // Rebinding the camera takes a moment on Android. Keeping the still up until the
            // first sample arrives avoids a black flash between release and first frame.
            _awaitingFirstFrame = true;
            _vm.Notice = string.Empty;
            _vm.MinZoom = 1.0;
            Camera.SetPreviewing(true);
        }
    }

    /// <summary>
    /// With the camera released, the only thing left for the zoom control to act on is the still,
    /// so it scales the image instead. Scaling about the centre keeps the measured patch centred:
    /// the zoom magnifies it rather than wandering off it. The reticle is scaled by the same
    /// factor so it goes on marking the area the held reading actually came from.
    ///
    /// Never below 1: the still is already cropped to the zoom it was captured at, and shrinking
    /// it would only pull its edges in from the screen.
    /// </summary>
    void ApplyFrozenZoom()
    {
        if (!FrozenFrame.IsVisible) return;

        var scale = _frozenZoom > 0 ? Math.Max(1.0, _vm.Zoom / _frozenZoom) : 1.0;
        FrozenFrame.Scale = scale;
        Reticle.Scale = scale;
    }

    void ClearFrozenFrame()
    {
        _awaitingFirstFrame = false;
        FrozenFrame.IsVisible = false;
        FrozenFrame.Scale = 1;
        Reticle.Scale = 1;
        FrozenFrame.Source = null;
    }

    bool _awaitingFirstFrame;

    /// <summary>Zoom the held still was captured at, i.e. the scale at which it reads 1:1.</summary>
    double _frozenZoom = 1.0;

    void OnColorSampled(object? sender, ColorSampledEventArgs e)
    {
        // First sample after Resume means frames are flowing again.
        if (_awaitingFirstFrame) ClearFrozenFrame();

        _vm.OnColorSampled(e.R, e.G, e.B);
    }

    void OnCameraError(object? sender, CameraErrorEventArgs e) => _vm.OnCameraError(e.Message);

    void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        // e.Scale is the factor since the previous update, so it accumulates multiplicatively.
        if (e.Status == GestureStatus.Running && e.Scale > 0)
            _vm.Zoom *= e.Scale;
    }

    // Double tap toggles between the bottom of the range and 2x into it. Both ends are relative
    // to MinZoom, which a held still raises off 1.0 - otherwise the gesture does nothing at all
    // while a frame captured at 4x is on screen.
    void OnDoubleTapped(object? sender, TappedEventArgs e) =>
        _vm.Zoom = _vm.Zoom > _vm.MinZoom * 1.01
            ? _vm.MinZoom
            : Math.Min(_vm.MinZoom * 2.0, _vm.MaxZoom);

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

#if ANDROID
        // The handle sits flush with the bottom edge, which is exactly where Android's
        // gesture navigation reserves an edge-swipe-to-home zone - without this, dragging the
        // handle gets hijacked by the OS and backgrounds the app instead of opening the panel.
        ExcludeHandleFromSystemGestures();
#endif
    }

#if ANDROID
    void ExcludeHandleFromSystemGestures()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return;
        if (PanelHandle.Handler?.PlatformView is not Android.Views.View view) return;
        if (view.Width <= 0 || view.Height <= 0) return;

        var location = new int[2];
        view.GetLocationOnScreen(location);
        var rect = new Android.Graphics.Rect(
            location[0], location[1], location[0] + view.Width, location[1] + view.Height);
        view.SystemGestureExclusionRects = [rect];
    }
#endif

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
        // Fire-and-forget, as before: the panel state is already committed above, the animations
        // just catch up to it.
        _ = ZoomBar.FadeToAsync(open ? 0 : 1, 150);
        ZoomBar.InputTransparent = open;
        _ = PanelContainer.TranslateToAsync(0, open ? 0 : _panelClosedOffset, 200, Easing.CubicOut);
    }
}
