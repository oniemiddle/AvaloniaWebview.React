using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.React.EmbeddedAssets;
using ReactHost.Frontend;

namespace ReactHost;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ReactView.RegisterApi<IHostApi>(new HostApi());

        ReactView.EnvironmentRequested += (_, args) =>
        {
            if (args is WindowsWebView2EnvironmentRequestedEventArgs webView2Args)
            {
                // Required for Avalonia controls (including the loading overlay) to compose above WebView2.
                webView2Args.ExperimentalOffscreen = true;
            }
        };

        ReactView.NavigationStarted += (_, _) =>
        {
            LoadingOverlay.IsVisible = true;
            LoadingIndicator.IsVisible = true;
            RetryButton.IsVisible = false;
            LoadingDetail.Text = "正在加载前端页面…";
        };

        ReactView.NavigationCompleted += (_, args) =>
        {
            if (!args.IsSuccess)
            {
                LoadingIndicator.IsVisible = false;
                LoadingDetail.Text = "页面加载失败，请检查前端服务或本地资源。";
                RetryButton.IsVisible = true;
            }
            else if (LoadingOverlay.IsVisible)
            {
                LoadingDetail.Text = "正在启动 React 应用…";
            }
        };

        // Configure the source before attaching: NativeWebView navigates as soon as it enters the visual tree.
        // Release serves the published bundle from wwwroot; Debug uses the Vite server unless overridden.
        var configuredUrl = Environment.GetEnvironmentVariable("AVALONIA_REACT_URL");
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            ReactView.Source = new Uri(configuredUrl, UriKind.Absolute);
        }
        else
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("AVALONIA_REACT_EMBEDDED") == "1")
            {
                ReactView.UseEmbeddedAssets(ConfigureEmbeddedAssets);
            }
            else
            {
                ReactView.Source = new Uri("http://127.0.0.1:5173/");
            }
#else
            ReactView.UseEmbeddedAssets(ConfigureEmbeddedAssets);
#endif
        }

        ReactView.MessageReceived += (_, message) =>
        {
            if (message.Type == "react:shell-ready")
            {
                LoadingOverlay.IsVisible = false;
            }
        };
    }

    private static void ConfigureEmbeddedAssets(EmbeddedAssetOptions options)
    {
        var transport = Environment.GetEnvironmentVariable("AVALONIA_REACT_NATIVE");
        if (transport == "1")
        {
            options.Transport = EmbeddedAssetTransport.NativeHost;
        }
        else if (string.Equals(transport, "auto", StringComparison.OrdinalIgnoreCase))
        {
            options.Transport = EmbeddedAssetTransport.Auto;
        }
    }

    private void RetryButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        LoadingOverlay.IsVisible = true;
        LoadingIndicator.IsVisible = true;
        RetryButton.IsVisible = false;
        LoadingDetail.Text = "正在重新加载前端页面…";
        ReactView.Refresh();
    }

    private sealed class HostApi : IHostApi
    {
        public Task<string> GetGreetingAsync(string name) => Task.FromResult($"Hello {name}, from the generated .NET API!");
    }
}
