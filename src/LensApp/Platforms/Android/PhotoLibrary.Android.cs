using Android.Content;
using Android.Media;
using Android.Provider;
using AndroidEnvironment = Android.OS.Environment;

namespace LensApp.Services;

public static partial class PhotoLibrary
{
    const string Album = "LensApp";

    public static partial string Location => $"Pictures/{Album}";

    public static partial async Task SaveJpegAsync(byte[] jpeg, string fileName)
    {
        var context = Platform.AppContext;

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            SaveViaMediaStore(context, jpeg, fileName);
            return;
        }

        // Before scoped storage, MediaStore could not be told where to put the file, so it is
        // written straight into the public Pictures folder - which needs the write permission
        // the manifest only asks for up to API 28.
        var status = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.StorageWrite>();
        if (status != PermissionStatus.Granted)
            throw new UnauthorizedAccessException("Storage permission denied - grant it in the system settings to save images.");

#pragma warning disable CA1422 // only reached below API 29, where these are the supported route
        var pictures = AndroidEnvironment.GetExternalStoragePublicDirectory(AndroidEnvironment.DirectoryPictures!)
                       ?? throw new IOException("No shared Pictures folder on this device.");
        var folder = new Java.IO.File(pictures, Album);
        folder.Mkdirs();

        var path = new Java.IO.File(folder, fileName).AbsolutePath;
        await File.WriteAllBytesAsync(path, jpeg);

        // Without a scan the image sits on disk but the gallery does not list it.
        MediaScannerConnection.ScanFile(context, [path], ["image/jpeg"], null);
#pragma warning restore CA1422
    }

    static void SaveViaMediaStore(Context context, byte[] jpeg, string fileName)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return;

        var resolver = context.ContentResolver ?? throw new IOException("No content resolver.");

        using var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, "image/jpeg");
        values.Put(MediaStore.IMediaColumns.RelativePath, $"{AndroidEnvironment.DirectoryPictures}/{Album}");

        // Pending keeps the gallery from showing a half-written file.
        values.Put(MediaStore.IMediaColumns.IsPending, 1);

        var collection = MediaStore.Images.Media.GetContentUri(MediaStore.VolumeExternalPrimary)
                         ?? throw new IOException("No external image collection.");
        var uri = resolver.Insert(collection, values)
                  ?? throw new IOException("The gallery refused the new image.");

        try
        {
            using (var output = resolver.OpenOutputStream(uri)
                                ?? throw new IOException("Could not open the new image for writing."))
            {
                output.Write(jpeg, 0, jpeg.Length);
            }

            values.Clear();
            values.Put(MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, values, null, null);
        }
        catch
        {
            // Do not leave an empty pending entry behind.
            try { resolver.Delete(uri, null, null); }
            catch { /* nothing more to do */ }
            throw;
        }
    }
}
