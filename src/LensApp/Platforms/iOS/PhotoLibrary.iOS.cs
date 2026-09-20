using Foundation;
using Photos;

namespace LensApp.Services;

public static partial class PhotoLibrary
{
    public static partial string Location => "Photos";

    public static partial async Task<SavedPhoto> SaveJpegAsync(byte[] jpeg, string fileName)
    {
        // Add-only: the app writes into the library but never gets to read it.
        var status = await PHPhotoLibrary.RequestAuthorizationAsync(PHAccessLevel.AddOnly);
        if (status is not (PHAuthorizationStatus.Authorized or PHAuthorizationStatus.Limited))
            throw new UnauthorizedAccessException("Photos access denied - allow adding photos in the system settings to save images.");

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var data = NSData.FromArray(jpeg);
        var identifier = string.Empty;

        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(
            () =>
            {
                var request = PHAssetCreationRequest.CreationRequestForAsset();
                request.AddResource(
                    PHAssetResourceType.Photo, data,
                    new PHAssetResourceCreationOptions { OriginalFilename = fileName });

                // The placeholder is the only identity the asset has until the block commits.
                identifier = request.PlaceholderForCreatedAsset?.LocalIdentifier ?? string.Empty;
            },
            (success, error) =>
            {
                if (success) done.TrySetResult();
                else done.TrySetException(new IOException(error?.LocalizedDescription ?? "Photos refused the image."));
            });

        await done.Task;

        // No path to quote: the library is a database, not a folder the user can browse to.
        return new SavedPhoto(Location, identifier);
    }

    public static partial async Task OpenAsync(SavedPhoto photo)
    {
        // Add-only access cannot open one asset, so this raises the Photos app and leaves the
        // user on its most recent shots - which is where the image just landed. Opened rather
        // than TryOpened on purpose: canOpenURL would need the scheme declared in Info.plist,
        // and an unverified guess there would fail silently instead of saying why.
        await Launcher.Default.OpenAsync("photos-redirect://");
    }
}
