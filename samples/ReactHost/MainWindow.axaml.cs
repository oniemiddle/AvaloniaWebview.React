using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
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
            LoadingOverlay.Background = new SolidColorBrush(Color.Parse("#F5F7FB"));
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

        var configuredUrl = Environment.GetEnvironmentVariable("AVALONIA_REACT_URL");
        var source = string.IsNullOrWhiteSpace(configuredUrl)
            ? new Uri("http://127.0.0.1:5173/")
            : new Uri(configuredUrl, UriKind.Absolute);

        ReactView.MessageReceived += (_, message) =>
        {
            if (message.Type == "react:shell-ready")
            {
                LoadingOverlay.IsVisible = false;
            }
        };

        // Configure the bridge and WebView environment first, then queue navigation immediately.
        // NativeWebView starts the browser as soon as it is attached to the window.
        ReactView.Source = source;
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
