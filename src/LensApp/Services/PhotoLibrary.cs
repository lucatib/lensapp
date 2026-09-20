namespace LensApp.Services;

/// <summary>An image that reached the gallery: where it landed, and how to reopen it.</summary>
/// <param name="FullPath">
/// Where to go and look for it. On Android that is the real filesystem path, file name included -
/// "Pictures/LensApp" alone leaves people hunting through a file manager. iOS has no path to
/// quote, because the Photos library is not a folder, so it names the library instead.
/// </param>
/// <param name="Handle">
/// Platform handle for <see cref="PhotoLibrary.OpenAsync"/>: the MediaStore content Uri on
/// Android, the PhotoKit local identifier on iOS. Empty when the platform gave nothing back, in
/// which case the image is still saved but cannot be opened from here.
/// </param>
public sealed record SavedPhoto(string FullPath, string Handle);

/// <summary>
/// Writes a finished JPEG into the device's photo gallery. Android goes through MediaStore into
/// Pictures/LensApp; iOS goes through PhotoKit with add-only access, so the app never asks to
/// read the user's library.
/// </summary>
public static partial class PhotoLibrary
{
    /// <summary>Where saved images end up: the full folder path on Android, "Photos" on iOS.</summary>
    public static partial string Location { get; }

    /// <summary>
    /// Saves <paramref name="jpeg"/> as <paramref name="fileName"/>. Throws
    /// <see cref="UnauthorizedAccessException"/> when the user refuses gallery access, and any
    /// other exception when the write itself fails.
    /// </summary>
    public static partial Task<SavedPhoto> SaveJpegAsync(byte[] jpeg, string fileName);

    /// <summary>
    /// Shows a saved image in whatever app the device uses for photos. Android opens that one
    /// image; iOS can only raise the Photos app itself, since add-only access does not extend to
    /// pointing at an asset.
    /// </summary>
    public static partial Task OpenAsync(SavedPhoto photo);
}
