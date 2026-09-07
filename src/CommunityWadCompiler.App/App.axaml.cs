using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommunityWadCompiler.App.Services;
using CommunityWadCompiler.App.Views;

namespace CommunityWadCompiler.App;

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
            string? arg = desktop.Args?.FirstOrDefault(a =>
                a.StartsWith(LanguageArgPrefix, StringComparison.OrdinalIgnoreCase));
            if (arg is not null)
                LanguageService.SetLanguage(arg[LanguageArgPrefix.Length..]);

            // Headless self-test used during development to verify the resource
            // dictionaries resolve after a language switch.
            if (desktop.Args?.Contains("--smoke-test") == true)
            {
                try
                {
                    string line = $"{LanguageService.CurrentLanguage}|" +
                                  $"{LanguageService.GetString("Menu.File")}|{LanguageService.GetString("Compile")}|" +
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