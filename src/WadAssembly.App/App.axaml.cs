using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WadAssembly.App.Services;
using WadAssembly.App.Views;
using WadAssembly.Core.Localization;

namespace WadAssembly.App;

public partial class App : Application
{
    private const string LanguageArgPrefix = "--language=";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Restore the persisted language, then let an explicit --language= argument win.
            LanguageService.LoadPersistedLanguage();
            // Route the Core's console messages through the app resource dictionaries so
            // logs follow the selected language (Spanish defaults when a key is missing).
            CoreMessages.Resolve = (key, fallback) => LanguageService.GetString(key, fallback);
            // Restore the persisted theme (system / light / dark).
            ThemeService.LoadPersistedTheme();
            string? arg = desktop.Args?.FirstOrDefault(a =>
                a.StartsWith(LanguageArgPrefix, StringComparison.OrdinalIgnoreCase));
            if (arg is not null)
                LanguageService.SetLanguage(arg[LanguageArgPrefix.Length..]);

            // Headless self-test used during development to verify the resource
            // dictionaries resolve after a language switch. Core messages are resolved
            // from a worker thread to reproduce the merge pipeline path.
            if (desktop.Args?.Contains("--smoke-test") == true)
            {
                string? background = null;
                var thread = new System.Threading.Thread(() =>
                    background = CoreMessages.Get("Merge.MusicRenamed", "D_RUNNIN", "a.wad", "D_RUNNIN2"));
                thread.Start();
                thread.Join();
                try
                {
                    string line = $"{LanguageService.CurrentLanguage}|" +
                                  $"{LanguageService.GetString("Menu.File")}|{LanguageService.GetString("Compile")}|" +
                                  $"bg=[{background}]|" +
                                  $"args=[{string.Join(",", desktop.Args ?? Array.Empty<string>())}]";
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "cwc_smoke.out"), line);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "cwc_smoke.out"), $"ERROR: {ex}");
                }
                desktop.Shutdown();
                Environment.Exit(0);
                return;
            }

            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}