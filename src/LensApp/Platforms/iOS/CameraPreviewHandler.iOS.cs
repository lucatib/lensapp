using AVFoundation;
using CoreFoundation;
using CoreMedia;
using CoreVideo;
using Foundation;
using LensApp.Models;
using ObjCRuntime;
using UIKit;

namespace LensApp.Handlers;

/// <summary>A UIView whose backing layer is the AVFoundation preview layer.</summary>
public sealed class CameraPreviewUIView : UIView
{
    [Export("layerClass")]
    public static Class GetLayerClass() => new(typeof(AVCaptureVideoPreviewLayer));

    public AVCaptureVideoPreviewLayer PreviewLayer => (AVCaptureVideoPreviewLayer)Layer;
}

/// <summary>
/// AVFoundation backend. A video data output runs alongside the preview layer so the centre
/// patch can be read from the raw BGRA frames; zoom and torch go through the capture device.
/// </summary>
public partial class CameraPreviewHandler
{
    AVCaptureSession? _session;
    AVCaptureDevice? _device;
    AVCaptureDeviceInput? _input;
    AVCaptureVideoDataOutput? _output;
    FrameDelegate? _frameDelegate;
    DispatchQueue? _queue;

    protected override CameraPreviewUIView CreatePlatformView() =>
        new() { BackgroundColor = UIColor.Black };

    protected override void ConnectHandler(CameraPreviewUIView platformView)
    {
        base.ConnectHandler(platformView);
        UpdateIsPreviewing();
    }

    protected override void DisconnectHandler(CameraPreviewUIView platformView)
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
        if (_session is not null || VirtualView is null) return;

        try
        {
            _device = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
            if (_device is null)
            {
                ReportError("No camera available on this device.");
                return;
            }

            _input = AVCaptureDeviceInput.FromDevice(_device, out var inputError);
            if (_input is null)
            {
                ReportError($"Could not open the camera: {inputError?.LocalizedDescription ?? "unknown error"}");
                return;
            }

            _session = new AVCaptureSession();
            _session.BeginConfiguration();

            if (_session.CanAddInput(_input))
            {
                _session.AddInput(_input);
            }
            else
            {
                _session.CommitConfiguration();
                ReportError("The camera input was rejected by the capture session.");
                Cleanup();
                return;
            }

            _queue = new DispatchQueue("com.qbtapp.lensapp.camera");
            _frameDelegate = new FrameDelegate(ReportColor)
            {
                Fraction = VirtualView.SampleSize,
                Rate = VirtualView.SampleRate,
            };

            _output = new AVCaptureVideoDataOutput
            {
                AlwaysDiscardsLateVideoFrames = true,
                WeakVideoSettings = new CVPixelBufferAttributes
                {
                    PixelFormatType = CVPixelFormatType.CV32BGRA,
                }.Dictionary,
            };
            _output.SetSampleBufferDelegate(_frameDelegate, _queue);

            if (_session.CanAddOutput(_output)) _session.AddOutput(_output);

            _session.CommitConfiguration();

            PlatformView.PreviewLayer.Session = _session;
            PlatformView.PreviewLayer.VideoGravity = AVLayerVideoGravity.ResizeAspectFill;
            ForcePortrait(PlatformView.PreviewLayer.Connection);
            ForcePortrait(_output.ConnectionFromMediaType(AVMediaTypes.Video.GetConstant()!));

            var session = _session;
            _queue.DispatchAsync(() =>
            {
                try { session.StartRunning(); }
                catch (Exception ex) { ReportError($"Could not start the camera: {ex.Message}"); }
            });

            var maxZoom = Math.Min((double)_device.ActiveFormat.VideoMaxZoomFactor, 20.0);
            ReportCapabilities(maxZoom, _device.HasTorch);

            UpdateZoom();
            UpdateTorch();
        }
        catch (Exception ex)
        {
            ReportError($"Could not start the camera: {ex.Message}");
            Cleanup();
        }
    }

    static void ForcePortrait(AVCaptureConnection? connection)
    {
        // The app is portrait-locked; keeping the connections explicit avoids a sideways
        // preview on devices that default to landscape.
        try
        {
            if (connection is { SupportsVideoOrientation: true })
                connection.VideoOrientation = AVCaptureVideoOrientation.Portrait;
        }
        catch
        {
            // Not fatal - the preview layer still renders with its own gravity.
        }
    }

    void StopCamera()
    {
        if (_session is null) return;

        SetTorch(false);

        var session = _session;
        try { session.StopRunning(); }
        catch { /* ignore teardown races */ }

        Cleanup();
    }

    void Cleanup()
    {
        try { _output?.SetSampleBufferDelegate(null!, null!); }
        catch { /* ignore */ }

        if (_session is not null && _input is not null)
        {
            try { _session.RemoveInput(_input); }
            catch { /* ignore */ }
        }

        // The handler may already be detached from its platform view by this point.
        try { PlatformView.PreviewLayer.Session = null; }
        catch { /* ignore */ }

        _frameDelegate?.Dispose();
        _frameDelegate = null;
        _output?.Dispose();
        _output = null;
        _input?.Dispose();
        _input = null;
        _session?.Dispose();
        _session = null;
        _queue?.Dispose();
        _queue = null;
        _device = null;
    }

    partial void UpdateZoom()
    {
        if (_device is null || VirtualView is null) return;

        var max = Math.Min((double)_device.ActiveFormat.VideoMaxZoomFactor, 20.0);
        var target = Math.Clamp(VirtualView.Zoom, 1.0, max);

        if (!_device.LockForConfiguration(out var error))
        {
            ReportError($"Zoom failed: {error?.LocalizedDescription ?? "device busy"}");
            return;
        }

        try { _device.VideoZoomFactor = (nfloat)target; }
        finally { _device.UnlockForConfiguration(); }
    }

    partial void UpdateTorch() => SetTorch(VirtualView?.IsTorchOn == true);

    void SetTorch(bool on)
    {
        if (_device is null || !_device.HasTorch) return;
        if (on && !_device.TorchAvailable) return;

        if (!_device.LockForConfiguration(out var error))
        {
            ReportError($"Torch failed: {error?.LocalizedDescription ?? "device busy"}");
            return;
        }

        try { _device.TorchMode = on ? AVCaptureTorchMode.On : AVCaptureTorchMode.Off; }
        catch (Exception ex) { ReportError($"Torch failed: {ex.Message}"); }
        finally { _device.UnlockForConfiguration(); }
    }

    /// <summary>Reads the centre patch out of each (throttled) BGRA frame.</summary>
    sealed class FrameDelegate : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        readonly Action<byte, byte, byte> _onColor;
        readonly PatchSampler _patch = new();
        long _lastTicks;

        public double Fraction { get; init; } = 0.07;
        public int Rate { get; init; } = 8;

        public FrameDelegate(Action<byte, byte, byte> onColor) => _onColor = onColor;

        public override void DidOutputSampleBuffer(
            AVCaptureOutput captureOutput, CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
        {
            try
            {
                var now = DateTime.UtcNow.Ticks;
                var minInterval = TimeSpan.TicksPerSecond / Math.Max(1, Rate);
                if (now - _lastTicks < minInterval) return;
                _lastTicks = now;

                using var imageBuffer = sampleBuffer.GetImageBuffer();
                if (imageBuffer is not CVPixelBuffer pixelBuffer) return;

                if (TrySample(pixelBuffer, out var r, out var g, out var b)) _onColor(r, g, b);
            }
            catch
            {
                // A dropped frame is not worth surfacing; the next one will be along shortly.
            }
            finally
            {
                sampleBuffer.Dispose();
            }
        }

        unsafe bool TrySample(CVPixelBuffer buffer, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;

            if (buffer.Lock(CVPixelBufferLock.ReadOnly) != CVReturn.Success) return false;

            try
            {
                var basePtr = (byte*)buffer.BaseAddress;
                var width = (int)buffer.Width;
                var height = (int)buffer.Height;
                var stride = (int)buffer.BytesPerRow;

                if (basePtr is null || width <= 0 || height <= 0) return false;

                var shortest = Math.Min(width, height);
                var side = Math.Clamp((int)(shortest * Fraction), 4, shortest);
                var left = (width - side) / 2;
                var top = (height - side) / 2;

                _patch.Reset(side * side);
                for (var y = 0; y < side; y++)
                {
                    var row = basePtr + (long)(top + y) * stride + (long)left * 4;
                    for (var x = 0; x < side; x++)
                    {
                        var px = row + x * 4;
                        _patch.Add(px[2], px[1], px[0]); // BGRA in memory
                    }
                }

                return _patch.TryGetAverage(out r, out g, out b);
            }
            finally
            {
                buffer.Unlock(CVPixelBufferLock.ReadOnly);
            }
        }
    }
}
