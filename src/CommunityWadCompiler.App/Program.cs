using Avalonia;

namespace CommunityWadCompiler.App;

internal static class Program
{
    // Initialization for the Avalonia desktop application. Do not use any Avalonia,
    // ReactiveUI, or DependencyProperty-involved code here: they belong in App.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect();
}