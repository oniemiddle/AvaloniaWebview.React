using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
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

        // Keep the pill in sync with the toolkit package version (0.19.0) instead of hardcoding it.
        var toolkitVersion = typeof(ReactHostView).Assembly.GetName().Version;
        VersionPillText.Text = toolkitVersion is null
            ? "v0.0.0"
            : toolkitVersion.Build >= 0
                ? $"v{toolkitVersion.Major}.{toolkitVersion.Minor}.{toolkitVersion.Build}"
                : $"v{toolkitVersion.Major}.{toolkitVersion.Minor}";

        if (session is not null)
        {
            ReactHost.Session = session;
        }

        HostApiBridge.Register(ReactHost.WebView, new SampleHostApi());
        UpdateRuntimeStatus();
        ReactHost.BridgeReady += (_, _) =>
        {
            HostStatusText.Text = $"{_runtimeSummary} · React 已就绪";
            ApplyStatusTone("SemiColorSuccess");
        };
    }

    private void UpdateRuntimeStatus()
    {
        var availability = ReactWebViewAvailability.Check();
        _runtimeSummary = availability.IsAvailable
            ? $"运行时：{availability.Engine} {availability.RuntimeVersion} · {availability.AdapterType}"
            : $"运行时不可用：{availability.Message} {availability.Guidance}";
        HostStatusText.Text = _runtimeSummary;
        ApplyStatusTone(availability.IsAvailable ? "SemiColorSuccess" : "SemiColorDanger");
    }

    // The status dot follows the Semi palette; if a token is missing the brush from XAML stays put.
    private void ApplyStatusTone(string resourceKey)
    {
        if (this.TryFindResource(resourceKey, out var resource) && resource is IBrush brush)
        {
            RuntimeStateDot.Fill = brush;
        }
    }

    // Native AOT: the payload is a string and the handler result is a string, both covered by the
    // generated resolver's built-in type infos, so no reflection-based JSON metadata is needed.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The payload and result are primitives covered by the generated JSON resolver.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "The payload and result are primitives covered by the generated JSON resolver.")]
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

    private async void HandleClearBrowserDataClick(object? sender, RoutedEventArgs e)
    {
        ClearDataButton.IsEnabled = false;
        try
        {
            var result = await ReactHost.ClearBrowserDataAsync(ReactWebViewDataKind.BrowserStorage);
            HostStatusText.Text = result.IsSuccess
                ? $"已清理浏览器存储：{result.Cleared}"
                : $"存储清理部分失败：{string.Join("; ", result.Warnings)}";
        }
        catch (Exception exception)
        {
            HostStatusText.Text = $"存储清理失败：{exception.Message}";
        }
        finally
        {
            ClearDataButton.IsEnabled = true;
        }
    }

    private void HandleOpenSecondWindowClick(object? sender, RoutedEventArgs e) =>
        new MainWindow().Show();

    private void HandleOpenIsolatedWindowClick(object? sender, RoutedEventArgs e) =>
        new MainWindow(ReactWebViewSession.CreateEphemeral()).Show();

    // Native AOT: ReactAck comes from FrontendApi.d.ts, so the generated resolver owns its metadata.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The payload is the generated ReactAck DTO, covered by the generated JSON resolver.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "The payload is the generated ReactAck DTO, covered by the generated JSON resolver.")]
    private void HandleMessageReceived(object? sender, ReactMessageReceivedEventArgs message)
    {
        if (message.Type != "react:clicked")
        {
            return;
        }

        var receivedAt = DateTimeOffset.Now;
        HostStatusText.Text = $"{_runtimeSummary}. 已收到 React 消息 {receivedAt:HH:mm:ss}";
        ApplyStatusTone("SemiColorSuccess");
        _ = ReactHost.PostMessageAsync("react:ack", new ReactAck { At = receivedAt.ToString("HH:mm:ss") });
    }
}
