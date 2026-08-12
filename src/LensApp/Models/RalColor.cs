namespace LensApp.Models;

/// <summary>A single RAL Classic entry with its sRGB approximation and cached CIE Lab value.</summary>
public sealed class RalColor
{
    public RalColor(string code, string name, byte r, byte g, byte b)
    {
        Code = code;
        Name = name;
        R = r;
        G = g;
        B = b;
        var lab = ColorMath.RgbToLab(r, g, b);
        L = lab.L;
        A = lab.A;
        Bb = lab.B;
    }

    public string Code { get; }
    public string Name { get; }
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    /// <summary>CIE L*</summary>
    public double L { get; }
    /// <summary>CIE a*</summary>
    public double A { get; }
    /// <summary>CIE b* (named Bb so it does not clash with the blue channel).</summary>
    public double Bb { get; }

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public Color ToMauiColor() => Color.FromRgb(R, G, B);

    public override string ToString() => $"{Code} {Name}";
}

/// <summary>A RAL candidate scored against a measured colour.</summary>
public sealed record RalMatch(RalColor Ral, double DeltaE)
{
    public string Code => Ral.Code;
    public string Name => Ral.Name;
    public string Hex => Ral.Hex;
    public Color Swatch => Ral.ToMauiColor();
    public string DeltaEText => $"ΔE {DeltaE:0.0}";

    /// <summary>Rough human reading of the CIEDE2000 distance.</summary>
    public string Quality => DeltaE switch
    {
        < 1.0 => "identical",
        < 2.0 => "excellent",
        < 3.5 => "good",
        < 5.0 => "fair",
        < 10.0 => "weak",
        _ => "poor",
    };
}
