namespace LensApp.Models;

/// <summary>
/// Averages the pixels of the reticle patch.
///
/// Two details matter for a colour reading: the average is taken in linear light (averaging
/// gamma-encoded bytes biases the result dark), and pixels whose luminance sits far from the
/// patch mean are dropped on a second pass so a specular highlight or a speck of dirt does not
/// drag the measurement.
/// </summary>
public sealed class PatchSampler
{
    byte[] _r = [];
    byte[] _g = [];
    byte[] _b = [];
    int _count;

    public int Count => _count;

    public void Reset(int capacity)
    {
        if (_r.Length < capacity)
        {
            _r = new byte[capacity];
            _g = new byte[capacity];
            _b = new byte[capacity];
        }

        _count = 0;
    }

    public void Add(byte r, byte g, byte b)
    {
        if (_count >= _r.Length) return;

        _r[_count] = r;
        _g[_count] = g;
        _b[_count] = b;
        _count++;
    }

    /// <summary>Trimmed mean of the collected pixels, in sRGB.</summary>
    public bool TryGetAverage(out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (_count == 0) return false;

        // Pass 1: mean and spread of the luminance.
        double sum = 0, sumSq = 0;
        for (var i = 0; i < _count; i++)
        {
            var y = Luma(i);
            sum += y;
            sumSq += y * y;
        }

        var mean = sum / _count;
        var variance = Math.Max(0.0, sumSq / _count - mean * mean);
        var sigma = Math.Sqrt(variance);

        // A perfectly flat patch has sigma ~ 0; keep a floor so we never reject everything.
        var tolerance = Math.Max(0.02, 1.5 * sigma);

        // Pass 2: average the pixels that survive, in linear light.
        double lr = 0, lg = 0, lb = 0;
        var kept = 0;
        for (var i = 0; i < _count; i++)
        {
            if (Math.Abs(Luma(i) - mean) > tolerance) continue;

            lr += ColorMath.ToLinear(_r[i]);
            lg += ColorMath.ToLinear(_g[i]);
            lb += ColorMath.ToLinear(_b[i]);
            kept++;
        }

        if (kept == 0) return false;

        r = ColorMath.FromLinear(lr / kept);
        g = ColorMath.FromLinear(lg / kept);
        b = ColorMath.FromLinear(lb / kept);
        return true;

        double Luma(int i) => ColorMath.Luminance(_r[i], _g[i], _b[i]);
    }
}
