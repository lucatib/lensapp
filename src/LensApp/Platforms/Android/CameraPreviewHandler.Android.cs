using Android.Graphics;
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

    protected override PreviewView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(PreviewView platformView)
    {
        base.ConnectHandler(platformView);
        UpdateIsPreviewing();
    }

    protected override void DisconnectHandler(PreviewView platformView)
    {
        StopCamera();
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

        _preview = new Preview.Builder().Build();
        _preview.SetSurfaceProvider(ContextCompat.GetMainExecutor(Context), PlatformView.SurfaceProvider);

        _camera = provider.BindToLifecycle(lifecycleOwner, CameraSelector.DefaultBackCamera, _preview);

        var maxZoom = 1.0;
        if (_camera.CameraInfo.ZoomState.Value is IZoomState zoomState)
            maxZoom = Math.Min(zoomState.MaxZoomRatio, 20.0);

        ReportCapabilities(maxZoom, _camera.CameraInfo.HasFlashUnit);

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
            if (_camera?.CameraInfo.HasFlashUnit == true)
                _camera.CameraControl.EnableTorch(false);
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

        try { _camera.CameraControl.SetZoomRatio((float)VirtualView.Zoom); }
        catch (Exception ex) { ReportError($"Zoom failed: {ex.Message}"); }
    }

    partial void UpdateTorch()
    {
        if (_camera is null || VirtualView is null) return;
        if (!_camera.CameraInfo.HasFlashUnit) return;

        try { _camera.CameraControl.EnableTorch(VirtualView.IsTorchOn); }
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

        Bitmap? bitmap = null;
        try
        {
            bitmap = PlatformView?.Bitmap;
            if (bitmap is { Width: > 0, Height: > 0 } && ReadCenterPatch(bitmap, VirtualView.SampleSize, out var r, out var g, out var b))
                ReportColor(r, g, b);
        }
        catch (Exception ex)
        {
            ReportError($"Colour sampling failed: {ex.Message}");
        }
        finally
        {
            bitmap?.Recycle();
            bitmap?.Dispose();
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
}
