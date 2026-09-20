using System.Globalization;
using LensApp.Models;
using Microsoft.Maui.Graphics.Platform;

namespace LensApp.Services;

/// <summary>What was on the readout when the snapshot was taken.</summary>
public sealed record SnapshotReading(
    Color Measured,
    string HexText,
    string LabText,
    RalMatch? Best,
    bool IsCalibrated);

/// <summary>
/// Turns a raw camera frame into the image that gets saved: the frame as framed on screen, the
/// reticle over the patch that was measured, and a caption strip with the reading. A system
/// screenshot cannot do this - the camera is composited outside the view hierarchy on both
/// platforms, so it comes out black - and a bare frame would lose the one thing worth keeping.
/// </summary>
public static class SnapshotRenderer
{
    const float JpegQuality = 0.92f;

    // Opaque, unlike the on-screen scrim. A translucent band is fine over a live preview, where
    // the eye tracks the moving image behind it, but in a still the frame's own detail freezes
    // underneath the text and competes with it - printed labels are the worst case, and they are
    // exactly what gets measured.
    static readonly Color Band = Color.FromArgb("#0E1116");
    static readonly Color Secondary = Color.FromArgb("#9AA7B8");
    static readonly Color Accent = Color.FromArgb("#4FC3F7");

    /// <param name="frameJpeg">The frame as the camera handler captured it.</param>
    /// <param name="scale">
    /// Extra magnification applied on screen, i.e. the digital zoom on a held still. The frame is
    /// cropped about its centre by this factor so the file matches what the user was looking at.
    /// </param>
    /// <param name="sampleFraction">Side of the sampled patch as a fraction of the short side.</param>
    public static byte[] Render(
        byte[] frameJpeg, double scale, double sampleFraction, SnapshotReading reading, DateTime takenAt)
    {
        using var input = new MemoryStream(frameJpeg);
        using var frame = PlatformImage.FromStream(input, ImageFormat.Jpeg);

        var width = (int)frame.Width;
        var height = (int)frame.Height;
        if (width <= 0 || height <= 0) throw new InvalidOperationException("The captured frame is empty.");

        using var context = new PlatformBitmapExportService().CreateContext(width, height, 1);
        var canvas = context.Canvas;

        var s = (float)Math.Max(1.0, scale);
        var drawnWidth = width * s;
        var drawnHeight = height * s;
        canvas.DrawImage(frame, (width - drawnWidth) / 2, (height - drawnHeight) / 2, drawnWidth, drawnHeight);

        // Everything else is laid out in units of 1/360 of the width, so the overlay reads the
        // same on a 720 px frame as on a 4K one.
        var u = width / 360f;

        DrawReticle(canvas, width, height, (float)(Math.Min(width, height) * sampleFraction * s), u);
        DrawCaption(canvas, width, height, u, reading, takenAt);

        using var output = new MemoryStream();
        context.Image.Save(output, ImageFormat.Jpeg, JpegQuality);
        return output.ToArray();
    }

    static void DrawReticle(ICanvas canvas, int width, int height, float side, float u)
    {
        side = Math.Max(side, 12 * u);
        var x = (width - side) / 2;
        var y = (height - side) / 2;

        // Same two-tone stroke as the on-screen reticle, so it shows on light and dark surfaces.
        canvas.StrokeColor = Color.FromRgba(0, 0, 0, 0.55);
        canvas.StrokeSize = 4 * u;
        canvas.DrawRoundedRectangle(x, y, side, side, 3 * u);

        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 2 * u;
        canvas.DrawRoundedRectangle(x, y, side, side, 3 * u);
    }

    static void DrawCaption(ICanvas canvas, int width, int height, float u, SnapshotReading reading, DateTime takenAt)
    {
        var pad = 12 * u;
        var swatch = 84 * u;
        var bandHeight = swatch + 2 * pad;
        var top = height - bandHeight;

        canvas.FillColor = Band;
        canvas.FillRectangle(0, top, width, bandHeight);

        // Measured on the left, the matched RAL on the right: the gap between the halves is the
        // ΔE made visible.
        var swatchX = pad;
        var swatchY = top + pad;
        var best = reading.Best;

        canvas.FillColor = reading.Measured;
        canvas.FillRectangle(swatchX, swatchY, best is null ? swatch : swatch / 2, swatch);
        if (best is not null)
        {
            canvas.FillColor = best.Swatch;
            canvas.FillRectangle(swatchX + swatch / 2, swatchY, swatch / 2, swatch);
        }

        canvas.StrokeColor = Color.FromRgba(255, 255, 255, 0.35);
        canvas.StrokeSize = Math.Max(1, u);
        canvas.DrawRectangle(swatchX, swatchY, swatch, swatch);

        var textX = swatchX + swatch + 14 * u;
        var textWidth = width - textX - pad;
        var y = swatchY - 2 * u;

        y = Line(canvas, best?.Code ?? reading.HexText, textX, y, textWidth, 22 * u, Colors.White, bold: true);
        y = Line(canvas, best?.Name ?? "no RAL match", textX, y, textWidth, 14 * u, Accent);

        if (best is not null)
            y = Line(canvas, $"ΔE00 {best.DeltaE:0.0} · {best.Quality}", textX, y, textWidth, 11 * u, Colors.White);

        y = Line(canvas, $"{reading.HexText} · {reading.LabText}", textX, y, textWidth, 11 * u, Secondary);

        var reference = reading.IsCalibrated ? "white ref set" : "no white ref";
        var stamp = takenAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Line(canvas, $"{stamp} · {reference}", textX, y, textWidth, 10 * u, Secondary);
    }

    /// <summary>Draws one line of text top-aligned at <paramref name="y"/>; returns the next line's y.</summary>
    static float Line(ICanvas canvas, string text, float x, float y, float width, float size, Color color, bool bold = false)
    {
        var lineHeight = size * 1.3f;

        canvas.Font = bold ? Microsoft.Maui.Graphics.Font.DefaultBold : Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = size;
        canvas.FontColor = color;
        canvas.DrawString(text, x, y, width, lineHeight, HorizontalAlignment.Left, VerticalAlignment.Top);

        return y + lineHeight;
    }
}
