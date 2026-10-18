using Avalonia.Controls;
using Avalonia.React;
using ReactHost.Frontend;

namespace ReactHost;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ReactView.RegisterApi<IHostApi>(new HostApi());
        ReactView.LoadStateChanged += HandleLoadStateChanged;
        ReactView.BridgeReady += (_, _) => LoadingOverlay.IsVisible = false;
        ReactView.MessageReceived += HandleMessageReceived;
    }

    private void HandleLoadStateChanged(object? sender, ReactLoadStateChangedEventArgs args)
    {
        // Keep the overlay up until the React client sends its ready handshake.
        LoadingOverlay.IsVisible = true;
        RetryButton.IsVisible = args.State == ReactLoadState.Failed;
        LoadingIndicator.IsVisible = args.State is ReactLoadState.Loading or ReactLoadState.Idle;
        LoadingDetail.Text = args.State switch
        {
            ReactLoadState.Idle => "正在初始化 WebView…",
            ReactLoadState.Loading => "正在加载前端页面…",
            ReactLoadState.Loaded => "正在启动 React 应用…",
            ReactLoadState.Failed => $"页面加载失败：{args.Error}",
            _ => string.Empty,
        };
    }

    private void HandleMessageReceived(object? sender, ReactMessageReceivedEventArgs message)
    {
        if (message.Type != "react:clicked")
        {
            return;
        }

        var receivedAt = DateTimeOffset.Now;
        Title = $"Avalonia + React · 已收到 React 消息 {receivedAt:HH:mm:ss}";
        _ = ReactView.PostMessageAsync("react:ack", new { at = receivedAt.ToString("HH:mm:ss") });
    }

    private void RetryButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        LoadingOverlay.IsVisible = true;
        LoadingDetail.Text = "正在重新加载前端页面…";
        ReactView.Reload();
    }

    private sealed class HostApi : IHostApi
    {
        public Task<string> GetGreetingAsync(string name) => Task.FromResult($"Hello {name}, from the generated .NET API!");
    }
}
