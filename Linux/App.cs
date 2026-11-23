using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace MeyertecPrint;

internal static class Desktop
{
    [STAThread]
    public static int Main(string[] args) =>
        AppBuilder.Configure<DesktopApp>().UsePlatformDetect().WithInterFont().LogToTrace()
            .StartWithClassicDesktopLifetime(args);
}

internal sealed class DesktopApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            L10n.Initialize(Path.Combine(AppContext.BaseDirectory, "Languages"));
            L10n.Load(L10n.PreferredLanguage() == "en" ? "en" : "ru");
            PrinterPlugins.Initialize(Path.Combine(AppContext.BaseDirectory, "Plugins"));
            desktop.MainWindow = new LinuxMainWindow(desktop.Args?.FirstOrDefault());
        }
        base.OnFrameworkInitializationCompleted();
    }
}
