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
}
