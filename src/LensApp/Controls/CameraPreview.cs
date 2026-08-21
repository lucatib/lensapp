namespace LensApp.Controls;

public sealed class ColorSampledEventArgs : EventArgs
{
    public ColorSampledEventArgs(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
}

public sealed class CameraErrorEventArgs : EventArgs
{
    public CameraErrorEventArgs(string message) => Message = message;
    public string Message { get; }
}

/// <summary>
/// Direct line to the platform handler, used where going through the property mapper is either
/// impossible or not worth trusting.
///
/// Frame capture has to live here because neither platform composites camera frames inside the
/// view hierarchy, so the grab must happen next to the camera pipeline. Start/stop is here for a
/// blunter reason: the mapper is fire-and-forget, and a stop that silently does not happen leaves
/// a live preview under a button that says Hold.
/// </summary>
public interface ICameraPreviewController
{
    /// <summary>The current frame as an image, or null if no frame is available yet.</summary>
    Task<ImageSource?> CaptureFrameAsync();

    /// <summary>Opens or releases the camera immediately, without waiting on a property change.</summary>
    void SetPreviewing(bool previewing);

    /// <summary>Whether the camera is actually open right now, as the handler sees it.</summary>
    bool IsCameraRunning { get; }
}

/// <summary>
/// Live camera preview with optical/digital zoom, torch control and continuous colour sampling
/// of a small patch at the centre of the frame. Backed by CameraX on Android and AVFoundation
/// on iOS - see <c>Handlers/CameraPreviewHandler.*</c>.
/// </summary>
public sealed class CameraPreview : View
{
    public static readonly BindableProperty IsPreviewingProperty = BindableProperty.Create(
        nameof(IsPreviewing), typeof(bool), typeof(CameraPreview), false);

    public static readonly BindableProperty ZoomProperty = BindableProperty.Create(
        nameof(Zoom), typeof(double), typeof(CameraPreview), 1.0, coerceValue: CoerceZoom);

    public static readonly BindableProperty MaxZoomProperty = BindableProperty.Create(
        nameof(MaxZoom), typeof(double), typeof(CameraPreview), 1.0);

    public static readonly BindableProperty IsTorchOnProperty = BindableProperty.Create(
        nameof(IsTorchOn), typeof(bool), typeof(CameraPreview), false);

    public static readonly BindableProperty IsTorchAvailableProperty = BindableProperty.Create(
        nameof(IsTorchAvailable), typeof(bool), typeof(CameraPreview), false);

    /// <summary>Side of the sampled square, as a fraction of the shorter frame dimension.</summary>
    public static readonly BindableProperty SampleSizeProperty = BindableProperty.Create(
        nameof(SampleSize), typeof(double), typeof(CameraPreview), 0.07);

    /// <summary>Samples per second requested from the platform pipeline.</summary>
    public static readonly BindableProperty SampleRateProperty = BindableProperty.Create(
        nameof(SampleRate), typeof(int), typeof(CameraPreview), 8);

    static object CoerceZoom(BindableObject bindable, object value)
    {
        var self = (CameraPreview)bindable;
        var z = (double)value;
        if (double.IsNaN(z) || z < 1.0) return 1.0;
        var max = self.MaxZoom < 1.0 ? 1.0 : self.MaxZoom;
        return z > max ? max : z;
    }

    public bool IsPreviewing
    {
        get => (bool)GetValue(IsPreviewingProperty);
        set => SetValue(IsPreviewingProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public double MaxZoom
    {
        get => (double)GetValue(MaxZoomProperty);
        private set => SetValue(MaxZoomProperty, value);
    }

    public bool IsTorchOn
    {
        get => (bool)GetValue(IsTorchOnProperty);
        set => SetValue(IsTorchOnProperty, value);
    }

    public bool IsTorchAvailable
    {
        get => (bool)GetValue(IsTorchAvailableProperty);
        private set => SetValue(IsTorchAvailableProperty, value);
    }

    public double SampleSize
    {
        get => (double)GetValue(SampleSizeProperty);
        set => SetValue(SampleSizeProperty, value);
    }

    public int SampleRate
    {
        get => (int)GetValue(SampleRateProperty);
        set => SetValue(SampleRateProperty, value);
    }

    public event EventHandler<ColorSampledEventArgs>? ColorSampled;
    public event EventHandler<CameraErrorEventArgs>? CameraError;

    // ---- called by the platform handlers (always on the UI thread) ---------------------

    internal void ReportColor(byte r, byte g, byte b) =>
        ColorSampled?.Invoke(this, new ColorSampledEventArgs(r, g, b));

    internal void ReportCapabilities(double maxZoom, bool torchAvailable)
    {
        MaxZoom = maxZoom < 1.0 ? 1.0 : maxZoom;
        IsTorchAvailable = torchAvailable;

        // Re-coerce in case the camera we opened supports less zoom than the previous one.
        if (Zoom > MaxZoom) Zoom = MaxZoom;
    }

    internal void ReportError(string message) =>
        CameraError?.Invoke(this, new CameraErrorEventArgs(message));

    /// <summary>
    /// Grabs the frame currently on screen. Returns null when the handler is not attached or the
    /// camera has not produced a frame yet.
    /// </summary>
    public Task<ImageSource?> CaptureFrameAsync() =>
        Handler is ICameraPreviewController controller
            ? controller.CaptureFrameAsync()
            : Task.FromResult<ImageSource?>(null);

    /// <summary>
    /// Opens or releases the camera. Sets <see cref="IsPreviewing"/> so bindings stay honest, then
    /// tells the handler directly rather than relying on the property mapper to relay it.
    /// </summary>
    public void SetPreviewing(bool previewing)
    {
        IsPreviewing = previewing;
        (Handler as ICameraPreviewController)?.SetPreviewing(previewing);
    }

    /// <summary>Whether the camera is open, according to the handler rather than to intent.</summary>
    public bool IsCameraRunning => (Handler as ICameraPreviewController)?.IsCameraRunning ?? false;
}
