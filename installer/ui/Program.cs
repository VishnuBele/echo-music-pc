using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace EchoMusicSetup;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<SetupApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

internal sealed class SetupApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new SetupWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
