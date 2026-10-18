using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaWebview.React;

namespace ReactHost;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ReactWebViewSession.Shared.ProfileName = "AvaloniaReactSample";
            desktop.MainWindow = new MainWindow();

            // Opt-in second window for the shared-session/shared-asset-server sample:
            // set AVALONIA_REACT_MULTI_WINDOW=1 before starting the app.
            if (Environment.GetEnvironmentVariable("AVALONIA_REACT_MULTI_WINDOW") == "1")
            {
                new MainWindow().Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
