using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaWebview.React;
using ReactHost.Frontend;

namespace ReactHost;

public sealed partial class MainWindow : Window
{
    private string _runtimeSummary = "正在检查 WebView 运行时…";

    public MainWindow()
        : this(session: null)
    {
    }

    public MainWindow(ReactWebViewSession? session)
    {
        InitializeComponent();

        if (session is not null)
        {
            ReactHost.Session = session;
        }

        HostApiBridge.Register(ReactHost.WebView, new HostApi());
        UpdateRuntimeStatus();
        ReactHost.BridgeReady += (_, _) => HostStatusText.Text = $"{_runtimeSummary} · React 已就绪";
    }

    private void UpdateRuntimeStatus()
    {
        var availability = ReactWebViewAvailability.Check();
        _runtimeSummary = availability.IsAvailable
            ? $"运行时：{availability.Engine} {availability.RuntimeVersion} · {availability.AdapterType}"
            : $"运行时不可用：{availability.Message} {availability.Guidance}";
        HostStatusText.Text = _runtimeSummary;
    }

    private async void HandleHostGreetClick(object? sender, RoutedEventArgs e)
    {
        HostGreetButton.IsEnabled = false;
        try
        {
            // .NET -> React request/response, handled by useReactHandler("host:greet", ...).
            var response = await ReactHost.InvokeReactAsync<string>(
                "host:greet",
                "Avalonia 原生工具栏",
                TimeSpan.FromSeconds(5));
            HostStatusText.Text = $"React 响应：{response}";
        }
        catch (Exception exception)
        {
            HostStatusText.Text = $"宿主 → React 失败：{exception.Message}";
        }
        finally
        {
            HostGreetButton.IsEnabled = true;
        }
    }

    private void HandleOpenSecondWindowClick(object? sender, RoutedEventArgs e) =>
        new MainWindow().Show();

    private void HandleOpenIsolatedWindowClick(object? sender, RoutedEventArgs e) =>
        new MainWindow(ReactWebViewSession.CreateEphemeral()).Show();

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
        public Task<string> GetGreetingAsync(string name) =>
            Task.FromResult($"Hello {name}, from the generated .NET API!");

        public Task<RuntimeSnapshot> GetRuntimeSnapshotAsync()
        {
            var availability = ReactWebViewAvailability.Check();
            return Task.FromResult(new RuntimeSnapshot
            {
                Host = "Avalonia 12",
                WebView = availability.IsAvailable
                    ? $"{availability.Engine} {availability.RuntimeVersion}".Trim()
                    : availability.Message,
                Health = availability.IsAvailable ? ServiceHealth.Healthy : ServiceHealth.Degraded,
                Tags = ["shared-session", "aot-json", "host-api"],
            });
        }
    }
}
