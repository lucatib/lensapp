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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (await EnsureCameraPermissionAsync()) Camera.IsPreviewing = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Releases the camera (and the torch) whenever the page is not on screen.
        Camera.IsPreviewing = false;
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

    void OnColorSampled(object? sender, ColorSampledEventArgs e) => _vm.OnColorSampled(e.R, e.G, e.B);

    void OnCameraError(object? sender, CameraErrorEventArgs e) => _vm.OnCameraError(e.Message);

    void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        // e.Scale is the factor since the previous update, so it accumulates multiplicatively.
        if (e.Status == GestureStatus.Running && e.Scale > 0)
            _vm.Zoom *= e.Scale;
    }

    void OnDoubleTapped(object? sender, TappedEventArgs e) =>
        _vm.Zoom = _vm.Zoom > 1.01 ? 1.0 : Math.Min(2.0, _vm.MaxZoom);

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
        ZoomBar.FadeTo(open ? 0 : 1, 150);
        ZoomBar.InputTransparent = open;
        PanelContainer.TranslateTo(0, open ? 0 : _panelClosedOffset, 200, Easing.CubicOut);
    }
}
