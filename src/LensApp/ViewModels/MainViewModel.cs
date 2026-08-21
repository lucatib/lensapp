using System.Collections.ObjectModel;
using System.Windows.Input;
using LensApp.Models;
using LensApp.Services;

namespace LensApp.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    /// <summary>Smoothing factor for the live readout. Lower = steadier, slower to settle.</summary>
    const double Smoothing = 0.25;

    readonly IRalMatcher _matcher;
    readonly WhiteBalanceService _whiteBalance;

    // Exponentially smoothed raw sensor sample, kept in linear light.
    double _rawR, _rawG, _rawB;
    bool _hasSample;

    byte _lastCorrectedR, _lastCorrectedG, _lastCorrectedB;

    public MainViewModel(IRalMatcher matcher, WhiteBalanceService whiteBalance)
    {
        _matcher = matcher;
        _whiteBalance = whiteBalance;

        ToggleTorchCommand = new Command(() => IsTorchOn = !IsTorchOn, () => IsTorchAvailable);
        ToggleFreezeCommand = new Command(() => IsFrozen = !IsFrozen);
        CalibrateCommand = new Command(Calibrate, () => _hasSample);
        ResetCalibrationCommand = new Command(ResetCalibration);
        CopyCommand = new Command(async () => await CopyAsync());

        IsCalibrated = _whiteBalance.IsCalibrated;
        Status = IsCalibrated
            ? "White reference in use."
            : "Set a white reference first - readings are guesswork without one.";
    }

    // ---- live measurement --------------------------------------------------------------

    Color _measuredColor = Colors.Black;
    public Color MeasuredColor
    {
        get => _measuredColor;
        private set => SetProperty(ref _measuredColor, value);
    }

    Color _measuredForeground = Colors.White;
    public Color MeasuredForeground
    {
        get => _measuredForeground;
        private set => SetProperty(ref _measuredForeground, value);
    }

    string _hexText = "—";
    public string HexText
    {
        get => _hexText;
        private set => SetProperty(ref _hexText, value);
    }

    string _rgbText = "waiting for the camera…";
    public string RgbText
    {
        get => _rgbText;
        private set => SetProperty(ref _rgbText, value);
    }

    string _labText = string.Empty;
    public string LabText
    {
        get => _labText;
        private set => SetProperty(ref _labText, value);
    }

    RalMatch? _bestMatch;
    public RalMatch? BestMatch
    {
        get => _bestMatch;
        private set
        {
            if (SetProperty(ref _bestMatch, value)) OnPropertyChanged(nameof(HasMatch));
        }
    }

    public bool HasMatch => BestMatch is not null;

    /// <summary>Alternatives, i.e. the matches after the best one.</summary>
    public ObservableCollection<RalMatch> Alternatives { get; } = [];

    // ---- camera state ------------------------------------------------------------------

    bool _isFrozen;
    public bool IsFrozen
    {
        get => _isFrozen;
        set
        {
            if (!SetProperty(ref _isFrozen, value)) return;

            OnPropertyChanged(nameof(FreezeLabel));
            Status = value
                ? "Reading held. Tap Hold again to resume."
                : "Live.";
        }
    }

    public string FreezeLabel => IsFrozen ? "Resume" : "Hold";

    bool _isTorchOn;
    public bool IsTorchOn
    {
        get => _isTorchOn;
        set
        {
            if (SetProperty(ref _isTorchOn, value)) OnPropertyChanged(nameof(TorchLabel));
        }
    }

    public string TorchLabel => IsTorchOn ? "Torch on" : "Torch off";

    bool _isTorchAvailable;
    public bool IsTorchAvailable
    {
        get => _isTorchAvailable;
        set
        {
            if (SetProperty(ref _isTorchAvailable, value)) ((Command)ToggleTorchCommand).ChangeCanExecute();
        }
    }

    double _zoom = 1.0;
    public double Zoom
    {
        get => _zoom;
        set
        {
            var clamped = Math.Clamp(value, MinZoom, Math.Max(MinZoom, MaxZoom));
            if (!SetProperty(ref _zoom, clamped)) return;

            OnPropertyChanged(nameof(ZoomText));
            OnPropertyChanged(nameof(ZoomFraction));
        }
    }

    /// <summary>
    /// Slider-friendly 0..1 view of the zoom, spanning <see cref="MinZoom"/> to
    /// <see cref="MaxZoom"/>. The mapping is geometric, so the lower half of the travel covers
    /// the small factors where framing actually happens. Kept normalised because a Slider
    /// refuses a Minimum that is not below its Maximum, and the real range is only known once
    /// the camera has opened.
    /// </summary>
    public double ZoomFraction
    {
        get
        {
            var span = ZoomSpan;
            return span <= 0 ? 0.0 : Math.Clamp((Math.Log(Zoom) - Math.Log(MinZoom)) / span, 0.0, 1.0);
        }
        set
        {
            var span = ZoomSpan;
            Zoom = span <= 0 ? MinZoom : MinZoom * Math.Exp(span * Math.Clamp(value, 0.0, 1.0));
        }
    }

    /// <summary>Width of the zoom range in log space, i.e. how far the slider travel spans.</summary>
    double ZoomSpan => Math.Log(Math.Max(MinZoom, MaxZoom)) - Math.Log(MinZoom);

    double _maxZoom = 1.0;
    public double MaxZoom
    {
        get => _maxZoom;
        set
        {
            if (!SetProperty(ref _maxZoom, value)) return;

            OnPropertyChanged(nameof(CanZoom));
            OnPropertyChanged(nameof(ZoomFraction));
            if (Zoom > value) Zoom = value;
        }
    }

    /// <summary>
    /// Floor of the zoom range, normally 1.0. While a held still is on screen it is raised to
    /// the zoom that still was captured at: the frame can only be scaled up from there, since
    /// no amount of scaling puts back the field of view the camera had already cropped away.
    /// </summary>
    double _minZoom = 1.0;
    public double MinZoom
    {
        get => _minZoom;
        set
        {
            var floor = Math.Max(1.0, value);
            if (!SetProperty(ref _minZoom, floor)) return;

            OnPropertyChanged(nameof(CanZoom));
            OnPropertyChanged(nameof(ZoomFraction));
            if (Zoom < floor) Zoom = floor;
        }
    }

    public bool CanZoom => MaxZoom > MinZoom * 1.01;

    public string ZoomText => $"{Zoom:0.0}×";

    /// <summary>
    /// Short warning shown over the preview. The status line lives inside the RAL panel, which
    /// starts closed, so anything the user needs to see right now goes here instead.
    /// </summary>
    string _notice = string.Empty;
    public string Notice
    {
        get => _notice;
        set
        {
            if (SetProperty(ref _notice, value)) OnPropertyChanged(nameof(HasNotice));
        }
    }

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    string _status = string.Empty;
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    bool _isCalibrated;
    public bool IsCalibrated
    {
        get => _isCalibrated;
        private set
        {
            if (SetProperty(ref _isCalibrated, value)) OnPropertyChanged(nameof(CalibrationLabel));
        }
    }

    public string CalibrationLabel => IsCalibrated ? "White ref set" : "No white ref";

    /// <summary>
    /// Which build is actually on the device. Debugging a camera by proxy is hopeless without
    /// it: a fix that never got installed and a fix that does not work look identical.
    /// </summary>
    public string BuildLabel => $"v{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

    // ---- commands ------------------------------------------------------------------------

    public ICommand ToggleTorchCommand { get; }
    public ICommand ToggleFreezeCommand { get; }
    public ICommand CalibrateCommand { get; }
    public ICommand ResetCalibrationCommand { get; }
    public ICommand CopyCommand { get; }

    // ---- sample pipeline -------------------------------------------------------------------

    /// <summary>Called on the UI thread for every frame sample coming out of the camera.</summary>
    public void OnColorSampled(byte r, byte g, byte b)
    {
        if (IsFrozen) return;

        // Smooth in linear light so the average is not pulled around by gamma.
        var lr = ColorMath.ToLinear(r);
        var lg = ColorMath.ToLinear(g);
        var lb = ColorMath.ToLinear(b);

        if (!_hasSample)
        {
            _rawR = lr;
            _rawG = lg;
            _rawB = lb;
            _hasSample = true;
            ((Command)CalibrateCommand).ChangeCanExecute();
        }
        else
        {
            _rawR += (lr - _rawR) * Smoothing;
            _rawG += (lg - _rawG) * Smoothing;
            _rawB += (lb - _rawB) * Smoothing;
        }

        Publish();
    }

    void Publish()
    {
        var rawR = ColorMath.FromLinear(_rawR);
        var rawG = ColorMath.FromLinear(_rawG);
        var rawB = ColorMath.FromLinear(_rawB);

        var (cr, cg, cb) = _whiteBalance.Apply(rawR, rawG, rawB);

        // Only redo the match when the reading actually moved - it keeps the list from
        // flickering between two near-identical candidates.
        if (cr == _lastCorrectedR && cg == _lastCorrectedG && cb == _lastCorrectedB) return;

        _lastCorrectedR = cr;
        _lastCorrectedG = cg;
        _lastCorrectedB = cb;

        MeasuredColor = Color.FromRgb(cr, cg, cb);
        MeasuredForeground = ColorMath.Luminance(cr, cg, cb) > 0.4 ? Colors.Black : Colors.White;
        HexText = $"#{cr:X2}{cg:X2}{cb:X2}";
        RgbText = $"R {cr}   G {cg}   B {cb}";

        var (l, a, bb) = ColorMath.RgbToLab(cr, cg, cb);
        LabText = $"L* {l:0.0}   a* {a:0.0}   b* {bb:0.0}";

        var matches = _matcher.Match(cr, cg, cb, 4);

        BestMatch = matches.Count > 0 ? matches[0] : null;

        Alternatives.Clear();
        for (var i = 1; i < matches.Count; i++) Alternatives.Add(matches[i]);
    }

    void Calibrate()
    {
        if (!_hasSample)
        {
            Status = "Nothing measured yet.";
            return;
        }

        var r = ColorMath.FromLinear(_rawR);
        var g = ColorMath.FromLinear(_rawG);
        var b = ColorMath.FromLinear(_rawB);

        var result = _whiteBalance.Calibrate(r, g, b);

        Status = result switch
        {
            CalibrationResult.Success => "White reference set. Readings are corrected for this light.",
            CalibrationResult.TooDark => "Too dark to use as a reference - add light and try again.",
            CalibrationResult.Clipped => "Blown out - move back from the light, or turn the torch off.",
            CalibrationResult.NotNeutral => "That is too saturated to be a white reference. Point at a white or grey card, not at the sample.",
            _ => "Nothing measured yet.",
        };

        if (result != CalibrationResult.Success) return;

        IsCalibrated = _whiteBalance.IsCalibrated;
        ForcePublish();
    }

    void ResetCalibration()
    {
        _whiteBalance.Reset();
        IsCalibrated = false;
        Status = "White reference cleared - showing raw sensor colour.";
        ForcePublish();
    }

    void ForcePublish()
    {
        // Invalidate the change filter so the readout refreshes with the new gains.
        _lastCorrectedR = _lastCorrectedG = _lastCorrectedB = 0;
        if (_hasSample) Publish();
    }

    async Task CopyAsync()
    {
        var best = BestMatch;
        var text = best is null
            ? HexText
            : $"{best.Code} {best.Name} ({best.Hex}) - measured {HexText}, ΔE00 {best.DeltaE:0.0}";

        try
        {
            await Clipboard.Default.SetTextAsync(text);
            Status = "Copied to the clipboard.";
        }
        catch (Exception ex)
        {
            Status = $"Could not copy: {ex.Message}";
        }
    }

    public void OnCameraError(string message)
    {
        Status = message;
        Notice = message;
    }
}
