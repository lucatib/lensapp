using Foundation;
using Photos;

namespace LensApp.Services;

public static partial class PhotoLibrary
{
    public static partial string Location => "Photos";

    public static partial async Task SaveJpegAsync(byte[] jpeg, string fileName)
    {
        // Add-only: the app writes into the library but never gets to read it.
        var status = await PHPhotoLibrary.RequestAuthorizationAsync(PHAccessLevel.AddOnly);
        if (status is not (PHAuthorizationStatus.Authorized or PHAuthorizationStatus.Limited))
            throw new UnauthorizedAccessException("Photos access denied - allow adding photos in the system settings to save images.");

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var data = NSData.FromArray(jpeg);

        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(
            () =>
            {
                var request = PHAssetCreationRequest.CreationRequestForAsset();
                request.AddResource(
                    PHAssetResourceType.Photo, data,
                    new PHAssetResourceCreationOptions { OriginalFilename = fileName });
            },
            (success, error) =>
            {
                if (success) done.TrySetResult();
                else done.TrySetException(new IOException(error?.LocalizedDescription ?? "Photos refused the image."));
            });

        await done.Task;
    }
}
