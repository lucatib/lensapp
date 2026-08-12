using LensApp.Models;

namespace LensApp.Services;

public interface IRalMatcher
{
    /// <summary>Returns the closest RAL entries to the given sRGB colour, best first.</summary>
    IReadOnlyList<RalMatch> Match(byte r, byte g, byte b, int count = 3);
}

public sealed class RalMatcher : IRalMatcher
{
    readonly IReadOnlyList<RalColor> _palette;

    public RalMatcher() : this(RalPalette.All) { }

    public RalMatcher(IReadOnlyList<RalColor> palette) => _palette = palette;

    public IReadOnlyList<RalMatch> Match(byte r, byte g, byte b, int count = 3)
    {
        if (count < 1) count = 1;

        var lab = ColorMath.RgbToLab(r, g, b);

        // Small palette (~210 entries): a full scan per frame is cheaper than any index.
        var best = new List<RalMatch>(count);
        foreach (var ral in _palette)
        {
            var de = ColorMath.DeltaE2000(lab, ral);

            if (best.Count < count)
            {
                best.Add(new RalMatch(ral, de));
                best.Sort(static (x, y) => x.DeltaE.CompareTo(y.DeltaE));
            }
            else if (de < best[^1].DeltaE)
            {
                best[^1] = new RalMatch(ral, de);
                best.Sort(static (x, y) => x.DeltaE.CompareTo(y.DeltaE));
            }
        }

        return best;
    }
}
