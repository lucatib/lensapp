using LensApp.Models;

namespace LensApp.Services;

/// <summary>Why a calibration attempt was refused, so the UI can say something useful.</summary>
public enum CalibrationResult
{
    Success,

    /// <summary>Nothing has been measured yet.</summary>
    NoSample,

    /// <summary>Under-exposed: the channel ratios carry no usable signal.</summary>
    TooDark,

    /// <summary>Blown out: clipped channels have lost their ratios.</summary>
    Clipped,

    /// <summary>Too saturated to be a white or grey card - almost certainly the sample itself.</summary>
    NotNeutral,
}

/// <summary>
/// Per-device grey-card correction.
///
/// A phone camera never hands back colorimetric values: the ISP applies its own white balance,
/// tone curve and saturation. Pointing the reticle at a neutral reference (a white sheet, a grey
/// card, RAL 9010) and calibrating stores channel gains that pull that reference back to neutral.
///
/// Note what this does NOT do: the reference keeps its measured brightness, so only the colour
/// cast is corrected, never the absolute lightness. On a neutral sample - aluminium, any grey -
/// hue carries no information and L* alone decides the match, so the reading stays at the mercy
/// of auto-exposure. Fixing that needs an exposure lock plus a reference of known lightness.
/// </summary>
public sealed class WhiteBalanceService
{
    const string KeyR = "wb_gain_r";
    const string KeyG = "wb_gain_g";
    const string KeyB = "wb_gain_b";
    const string KeyCalibrated = "wb_calibrated";

    /// <summary>
    /// Chroma ceiling for something claimed to be neutral. Measured against the palette: a white
    /// card under strong tungsten reaches C* 28, while the least saturated real colour in RAL
    /// Classic (5015 Sky blue) sits at 42, so 32 separates a badly lit reference from a mistake.
    ///
    /// Pale ivories and beiges (RAL 1013, 1014, C* 10-25) fall inside the accepted band and are
    /// genuinely indistinguishable from a cast white card - no threshold can separate those two
    /// without knowing the illuminant. This catches gross mistakes, not subtle ones.
    /// </summary>
    const double MaxReferenceChroma = 32.0;

    public double GainR { get; private set; } = 1.0;
    public double GainG { get; private set; } = 1.0;
    public double GainB { get; private set; } = 1.0;

    /// <summary>
    /// Whether a reference has been set. Tracked explicitly rather than inferred from the gains:
    /// a perfectly neutral reference yields gains of exactly 1.0, which is a successful
    /// calibration that the old "any gain != 1" test reported as uncalibrated.
    /// </summary>
    public bool IsCalibrated { get; private set; }

    public WhiteBalanceService() => Load();

    void Load()
    {
        GainR = Preferences.Default.Get(KeyR, 1.0);
        GainG = Preferences.Default.Get(KeyG, 1.0);
        GainB = Preferences.Default.Get(KeyB, 1.0);
        IsCalibrated = Preferences.Default.Get(KeyCalibrated, false);
    }

    /// <summary>
    /// Calibrate from a sample of a neutral reference. The reference luminance is preserved, only
    /// the colour cast is removed.
    /// </summary>
    public CalibrationResult Calibrate(byte r, byte g, byte b)
    {
        if (r < 24 || g < 24 || b < 24) return CalibrationResult.TooDark;
        if (r > 250 && g > 250 && b > 250) return CalibrationResult.Clipped;

        var (_, a, bb) = ColorMath.RgbToLab(r, g, b);
        if (Math.Sqrt(a * a + bb * bb) > MaxReferenceChroma) return CalibrationResult.NotNeutral;

        var target = (r + g + b) / 3.0;
        var gr = target / r;
        var gg = target / g;
        var gb = target / b;

        // Belt and braces: the chroma test above should already have caught anything this extreme.
        if (gr is < 0.5 or > 2.0 || gg is < 0.5 or > 2.0 || gb is < 0.5 or > 2.0)
            return CalibrationResult.NotNeutral;

        GainR = gr;
        GainG = gg;
        GainB = gb;
        IsCalibrated = true;
        Persist();
        return CalibrationResult.Success;
    }

    public void Reset()
    {
        GainR = GainG = GainB = 1.0;
        IsCalibrated = false;
        Persist();
    }

    void Persist()
    {
        Preferences.Default.Set(KeyR, GainR);
        Preferences.Default.Set(KeyG, GainG);
        Preferences.Default.Set(KeyB, GainB);
        Preferences.Default.Set(KeyCalibrated, IsCalibrated);
    }

    public (byte R, byte G, byte B) Apply(byte r, byte g, byte b) =>
        (ColorMath.Clamp8(r * GainR), ColorMath.Clamp8(g * GainG), ColorMath.Clamp8(b * GainB));
}
