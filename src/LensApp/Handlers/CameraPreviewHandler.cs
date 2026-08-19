using LensApp.Controls;
using Microsoft.Maui.Handlers;

#if ANDROID
using PlatformCameraView = AndroidX.Camera.View.PreviewView;
#elif IOS
using PlatformCameraView = LensApp.Handlers.CameraPreviewUIView;
#endif

namespace LensApp.Handlers;

public partial class CameraPreviewHandler : ViewHandler<CameraPreview, PlatformCameraView>, ICameraFrameCapture
{
    public static readonly IPropertyMapper<CameraPreview, CameraPreviewHandler> CameraMapper =
        new PropertyMapper<CameraPreview, CameraPreviewHandler>(ViewMapper)
        {
            [nameof(CameraPreview.IsPreviewing)] = static (h, _) => h.UpdateIsPreviewing(),
            [nameof(CameraPreview.Zoom)] = static (h, _) => h.UpdateZoom(),
            [nameof(CameraPreview.IsTorchOn)] = static (h, _) => h.UpdateTorch(),
        };

    public CameraPreviewHandler() : base(CameraMapper) { }

    partial void UpdateIsPreviewing();
    partial void UpdateZoom();
    partial void UpdateTorch();

    /// <summary>Marshals a callback from a camera thread onto the UI thread.</summary>
    void OnUiThread(Action action)
    {
        var dispatcher = VirtualView?.Dispatcher;
        if (dispatcher is null) return;

        if (dispatcher.IsDispatchRequired) dispatcher.Dispatch(action);
        else action();
    }

    void ReportColor(byte r, byte g, byte b) =>
        OnUiThread(() => VirtualView?.ReportColor(r, g, b));

    void ReportCapabilities(double maxZoom, bool torchAvailable) =>
        OnUiThread(() => VirtualView?.ReportCapabilities(maxZoom, torchAvailable));

    void ReportError(string message) =>
        OnUiThread(() => VirtualView?.ReportError(message));
}
