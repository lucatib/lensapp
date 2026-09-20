using Android.Content;
using Android.Media;
using Android.Provider;
using AndroidEnvironment = Android.OS.Environment;
using AndroidUri = Android.Net.Uri;

namespace LensApp.Services;

public static partial class PhotoLibrary
{
    const string Album = "LensApp";

    public static partial string Location => FolderPath;

    /// <summary>
    /// The album as a file manager would show it, e.g. <c>/storage/emulated/0/Pictures/LensApp</c>.
    /// The public-directory call is deprecated for <em>writing</em> under scoped storage, but it
    /// remains the only way to learn the real path, and the path is the thing worth telling the
    /// user - the relative form sends people hunting.
    /// </summary>
    static string FolderPath
    {
        get
        {
#pragma warning disable CA1422 // read only: nothing is written through this path on API 29+
            var pictures = AndroidEnvironment
                .GetExternalStoragePublicDirectory(AndroidEnvironment.DirectoryPictures!)?.AbsolutePath;
#pragma warning restore CA1422

            return string.IsNullOrEmpty(pictures)
                ? $"{AndroidEnvironment.DirectoryPictures}/{Album}"
                : $"{pictures}/{Album}";
        }
    }

    public static partial async Task<SavedPhoto> SaveJpegAsync(byte[] jpeg, string fileName)
    {
        var context = Platform.AppContext;
        var path = $"{FolderPath}/{fileName}";

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            return new SavedPhoto(path, SaveViaMediaStore(context, jpeg, fileName));

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

        path = new Java.IO.File(folder, fileName).AbsolutePath;
        await File.WriteAllBytesAsync(path, jpeg);
#pragma warning restore CA1422

        // Without a scan the image sits on disk but the gallery does not list it.
        return new SavedPhoto(path, await ScanAsync(context, path));
    }

    /// <returns>The MediaStore content Uri of the new image.</returns>
    static string SaveViaMediaStore(Context context, byte[] jpeg, string fileName)
    {
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

        return uri.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Runs the media scanner and waits for the content Uri it hands back. Nothing can be opened
    /// without one: a <c>file://</c> Uri handed to another app trips FileUriExposedException. The
    /// file is already on disk by this point, so a scanner that never answers costs the Open
    /// action, not the save.
    /// </summary>
    static async Task<string> ScanAsync(Context context, string path)
    {
        var scanned = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        MediaScannerConnection.ScanFile(context, [path], ["image/jpeg"], new ScanListener(scanned));

        try
        {
            return await scanned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            return string.Empty;
        }
    }

    sealed class ScanListener(TaskCompletionSource<string> scanned)
        : Java.Lang.Object, MediaScannerConnection.IOnScanCompletedListener
    {
        public void OnScanCompleted(string? path, AndroidUri? uri) =>
            scanned.TrySetResult(uri?.ToString() ?? string.Empty);
    }

    public static partial Task OpenAsync(SavedPhoto photo)
    {
        if (string.IsNullOrEmpty(photo.Handle))
            throw new IOException("The gallery has no entry for that image yet.");

        var uri = AndroidUri.Parse(photo.Handle)
                  ?? throw new IOException("That image's gallery entry is unreadable.");

        using var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "image/jpeg");

        // The viewer is a different app: it gets read access to this one image and nothing else,
        // and it needs its own task because AppContext is not an activity.
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);

        var context = (Context?)Platform.CurrentActivity ?? Platform.AppContext;
        context.StartActivity(intent);

        return Task.CompletedTask;
    }
}
