using Android.Graphics;
using Android.Runtime;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using LensApp.Models;

namespace LensApp.Handlers;

/// <summary>
/// CameraX backend. The preview use case is bound to the activity lifecycle, and the colour
/// sample is read straight off the rendered preview (<see cref="PreviewView.Bitmap"/>) so what
/// gets measured is exactly what the reticle shows, zoom included.
/// </summary>
public partial class CameraPreviewHandler
{
    ProcessCameraProvider? _provider;
    ICamera? _camera;
    Preview? _preview;
    bool _sampling;
    bool _binding;
    int _sampleGeneration;
    readonly PatchSampler _patch = new();

    // The most recent preview frame, kept so Hold always has a still to show. Sampling and
    // capture both run on the UI thread, so this needs no locking.
    Bitmap? _lastFrame;

    protected override PreviewView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(PreviewView platformView)
    {
        base.ConnectHandler(platformView);
        UpdateIsPreviewing();
    }

    protected override void DisconnectHandler(PreviewView platformView)
    {
        StopCamera();
        ReleaseLastFrame();
        base.DisconnectHandler(platformView);
    }

    partial void UpdateIsPreviewing()
    {
        if (VirtualView?.IsPreviewing == true) StartCamera();
        else StopCamera();
    }

    void StartCamera()
    {
        if (_camera is not null || _binding) return;

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is not ILifecycleOwner lifecycleOwner)
        {
            ReportError("No activity available to host the camera.");
            return;
        }

        _binding = true;
        var context = Context;
        var future = ProcessCameraProvider.GetInstance(context);

        future.AddListener(new Java.Lang.Runnable(() =>
        {
            try
            {
                _provider = (ProcessCameraProvider?)future.Get();
                if (_provider is null || VirtualView?.IsPreviewing != true) return;

                Bind(_provider, lifecycleOwner);
            }
            catch (Exception ex)
            {
                ReportError($"Could not start the camera: {ex.Message}");
            }
            finally
            {
                _binding = false;
            }
        }), ContextCompat.GetMainExecutor(context));
    }

    void Bind(ProcessCameraProvider provider, ILifecycleOwner lifecycleOwner)
    {
        provider.UnbindAll();

        // The CameraX bindings surface most of this as nullable, so each step is checked rather
        // than assumed — a null here means the device denied the use case, not a bug.
        var preview = new Preview.Builder().Build();
        if (preview is null)
        {
            ReportError("Could not create the camera preview.");
            return;
        }

        preview.SetSurfaceProvider(ContextCompat.GetMainExecutor(Context), PlatformView.SurfaceProvider);
        _preview = preview;

        if (CameraSelector.DefaultBackCamera is not { } backCamera)
        {
            ReportError("This device has no back camera.");
            return;
        }

        _camera = provider.BindToLifecycle(lifecycleOwner, backCamera, preview);
        if (_camera is null)
        {
            ReportError("Could not bind the camera to the activity lifecycle.");
            return;
        }

        var maxZoom = 1.0;
        if (_camera.CameraInfo?.ZoomState?.Value is { } zoomValue)
        {
            // The camera2 adapter hands back androidx.camera.camera2.adapter.ZoomValue, and that
            // binding does not declare IZoomState on the managed side - a plain `is IZoomState`
            // silently fails there even though the Java object implements the interface, which
            // left maxZoom at 1.0 and hid the zoom bar. JavaCast goes through the JNI type.
            try
            {
                maxZoom = Math.Min(zoomValue.JavaCast<IZoomState>().MaxZoomRatio, 20.0);
            }
            catch (InvalidCastException)
            {
                // Genuinely not a ZoomState - leave the 1.0 fallback in place.
            }
        }

        ReportCapabilities(maxZoom, _camera.CameraInfo?.HasFlashUnit == true);

        // Re-apply whatever the view already had set while the camera was closed.
        UpdateZoom();
        UpdateTorch();
        StartSampling();
    }

    void StopCamera()
    {
        _sampling = false;
        _sampleGeneration++;

        try
        {
            if (_camera?.CameraInfo?.HasFlashUnit == true)
                _camera.CameraControl?.EnableTorch(false);
        }
        catch { /* the camera may already be gone */ }

        try { _provider?.UnbindAll(); }
        catch { /* ignore teardown races */ }

        _camera = null;
        _preview = null;
    }

    partial void UpdateZoom()
    {
        if (_camera is null || VirtualView is null) return;

        try { _camera.CameraControl?.SetZoomRatio((float)VirtualView.Zoom); }
        catch (Exception ex) { ReportError($"Zoom failed: {ex.Message}"); }
    }

    partial void UpdateTorch()
    {
        if (_camera is null || VirtualView is null) return;
        if (_camera.CameraInfo?.HasFlashUnit != true) return;

        try { _camera.CameraControl?.EnableTorch(VirtualView.IsTorchOn); }
        catch (Exception ex) { ReportError($"Torch failed: {ex.Message}"); }
    }

    // ---- colour sampling ---------------------------------------------------------------

    void StartSampling()
    {
        // Bump the generation so any callback still queued from a previous run dies quietly
        // instead of leaving two sampling loops racing each other.
        _sampleGeneration++;
        _sampling = true;
        ScheduleSample();
    }

    void ScheduleSample()
    {
        if (!_sampling) return;

        var generation = _sampleGeneration;
        var rate = Math.Max(1, VirtualView?.SampleRate ?? 8);
        var delayMs = Math.Max(50, 1000 / rate);
        PlatformView?.PostDelayed(() => SampleOnce(generation), delayMs);
    }

    void SampleOnce(int generation)
    {
        if (!_sampling || generation != _sampleGeneration || VirtualView is null) return;

        try
        {
            var bitmap = PlatformView?.Bitmap;
            if (bitmap is { Width: > 0, Height: > 0 })
            {
                // Retain the newest frame and drop the one it replaces, so at most one preview
                // bitmap is alive at a time - the same churn as before, one cycle later.
                ReleaseLastFrame();
                _lastFrame = bitmap;

                if (ReadCenterPatch(bitmap, VirtualView.SampleSize, out var r, out var g, out var b))
                    ReportColor(r, g, b);
            }
            else
            {
                bitmap?.Dispose();
            }
        }
        catch (Exception ex)
        {
            ReportError($"Colour sampling failed: {ex.Message}");
        }
        finally
        {
            ScheduleSample();
        }
    }

    bool ReadCenterPatch(Bitmap bitmap, double fraction, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;

        var shortest = Math.Min(bitmap.Width, bitmap.Height);
        if (shortest < 4) return false;

        var side = Math.Clamp((int)(shortest * fraction), 4, shortest);
        var left = (bitmap.Width - side) / 2;
        var top = (bitmap.Height - side) / 2;

        var pixels = new int[side * side];
        bitmap.GetPixels(pixels, 0, side, left, top, side, side);

        _patch.Reset(pixels.Length);
        foreach (var pixel in pixels)
        {
            _patch.Add(
                (byte)((pixel >> 16) & 0xFF),
                (byte)((pixel >> 8) & 0xFF),
                (byte)(pixel & 0xFF));
        }

        return _patch.TryGetAverage(out r, out g, out b);
    }

    // ---- frame capture -----------------------------------------------------------------

    /// <summary>
    /// Lifts the frame currently rendered by the PreviewView. This is the same surface the
    /// colour sample is read from, so the still matches the last reading exactly.
    /// Must be called on the UI thread - PreviewView.Bitmap requires it.
    /// </summary>
    public Task<ImageSource?> CaptureFrameAsync()
    {
        try
        {
            // Prefer the frame the last reading came from: it is guaranteed to exist once
            // sampling has started, and it is exactly what the reticle measured. Asking
            // PreviewView for a fresh bitmap can come back null depending on when it is called.
            var bitmap = _lastFrame;
            var borrowed = false;

            if (bitmap is null || bitmap.IsRecycled)
            {
                bitmap = PlatformView?.Bitmap;
                borrowed = true;
            }

            if (bitmap is not { Width: > 0, Height: > 0 })
            {
                if (borrowed) bitmap?.Dispose();
                return Task.FromResult<ImageSource?>(null);
            }

            using var stream = new MemoryStream();
            bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 92, stream);
            var bytes = stream.ToArray();

            if (borrowed)
            {
                bitmap.Recycle();
                bitmap.Dispose();
            }

            if (bytes.Length == 0) return Task.FromResult<ImageSource?>(null);

            // ImageSource.FromStream is invoked lazily and possibly more than once, so it gets a
            // fresh stream over the bytes each time rather than a captured one.
            return Task.FromResult<ImageSource?>(ImageSource.FromStream(() => new MemoryStream(bytes)));
        }
        catch (Exception ex)
        {
            ReportError($"Could not freeze the frame: {ex.Message}");
            return Task.FromResult<ImageSource?>(null);
        }
    }

    void ReleaseLastFrame()
    {
        if (_lastFrame is null) return;

        if (!_lastFrame.IsRecycled) _lastFrame.Recycle();
        _lastFrame.Dispose();
        _lastFrame = null;
    }
}
