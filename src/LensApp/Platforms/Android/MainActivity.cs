using Android.App;
using Android.Content.PM;
using Android.OS;

namespace LensApp;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    // Portrait only: the reticle-to-sample mapping assumes a portrait preview.
    ScreenOrientation = ScreenOrientation.Portrait,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // A colour reading is useless if the screen dims mid-measurement.
        Window?.AddFlags(Android.Views.WindowManagerFlags.KeepScreenOn);
    }
}
