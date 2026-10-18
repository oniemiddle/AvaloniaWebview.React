using Avalonia.Controls;
using Avalonia.React;
using ReactHost.Frontend;

namespace ReactHost;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        HostApiBridge.Register(ReactHost.WebView, new HostApi());
    }

    private void HandleMessageReceived(object? sender, ReactMessageReceivedEventArgs message)
    {
        if (message.Type != "react:clicked")
        {
            return;
        }

        var receivedAt = DateTimeOffset.Now;
        Title = $"Avalonia + React · 已收到 React 消息 {receivedAt:HH:mm:ss}";
        _ = ReactHost.PostMessageAsync("react:ack", new { at = receivedAt.ToString("HH:mm:ss") });
    }

    private sealed class HostApi : IHostApi
    {
        public Task<string> GetGreetingAsync(string name) => Task.FromResult($"Hello {name}, from the generated .NET API!");
    }
}
