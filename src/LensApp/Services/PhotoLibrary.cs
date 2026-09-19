namespace LensApp.Services;

/// <summary>
/// Writes a finished JPEG into the device's photo gallery. Android goes through MediaStore into
/// Pictures/LensApp; iOS goes through PhotoKit with add-only access, so the app never asks to
/// read the user's library.
/// </summary>
public static partial class PhotoLibrary
{
    /// <summary>Where saved images end up, in words the user will recognise.</summary>
    public static partial string Location { get; }

    /// <summary>
    /// Saves <paramref name="jpeg"/> as <paramref name="fileName"/>. Throws
    /// <see cref="UnauthorizedAccessException"/> when the user refuses gallery access, and any
    /// other exception when the write itself fails.
    /// </summary>
    public static partial Task SaveJpegAsync(byte[] jpeg, string fileName);
}
