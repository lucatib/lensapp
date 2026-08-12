using LensApp.Models;

namespace LensApp.Services;

/// <summary>
/// Per-device grey-card correction.
///
/// A phone camera never hands back colorimetric values: the ISP applies its own white balance,
/// tone curve and saturation. Pointing the reticle at a neutral reference (a white sheet, a grey
/// card, RAL 9010) and calibrating stores channel gains that pull that reference back to neutral,
/// which is what makes the RAL match worth anything.
/// </summary>
public sealed class WhiteBalanceService
{
    const string KeyR = "wb_gain_r";
    const string KeyG = "wb_gain_g";
    const string KeyB = "wb_gain_b";

    public double GainR { get; private set; } = 1.0;
    public double GainG { get; private set; } = 1.0;
    public double GainB { get; private set; } = 1.0;

    public bool IsCalibrated => GainR != 1.0 || GainG != 1.0 || GainB != 1.0;

    public WhiteBalanceService() => Load();

    void Load()
    {
        GainR = Preferences.Default.Get(KeyR, 1.0);
        GainG = Preferences.Default.Get(KeyG, 1.0);
        GainB = Preferences.Default.Get(KeyB, 1.0);
    }

    /// <summary>
    /// Calibrate from a sample of a neutral reference. The reference luminance is preserved, only
    /// the colour cast is removed. Extreme gains are rejected - a badly exposed sample (blown out
    /// or nearly black) carries no usable ratio.
    /// </summary>
    public bool Calibrate(byte r, byte g, byte b)
    {
        if (r < 24 || g < 24 || b < 24) return false;       // too dark to divide by
        if (r > 250 && g > 250 && b > 250) return false;    // clipped

        var target = (r + g + b) / 3.0;
        var gr = target / r;
        var gg = target / g;
        var gb = target / b;

        if (gr is < 0.5 or > 2.0 || gg is < 0.5 or > 2.0 || gb is < 0.5 or > 2.0) return false;

        GainR = gr;
        GainG = gg;
        GainB = gb;
        Persist();
        return true;
    }

    public void Reset()
    {
        GainR = GainG = GainB = 1.0;
        Persist();
    }

    void Persist()
    {
        Preferences.Default.Set(KeyR, GainR);
        Preferences.Default.Set(KeyG, GainG);
        Preferences.Default.Set(KeyB, GainB);
    }

    public (byte R, byte G, byte B) Apply(byte r, byte g, byte b) =>
        (ColorMath.Clamp8(r * GainR), ColorMath.Clamp8(g * GainG), ColorMath.Clamp8(b * GainB));
}
