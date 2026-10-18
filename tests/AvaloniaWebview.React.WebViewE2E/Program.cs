using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using AvaloniaWebview.React;

namespace AvaloniaWebview.React.WebViewE2E;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var resultPath = Environment.GetEnvironmentVariable("AVALONIA_REACT_E2E_RESULT")
            ?? Path.Combine(AppContext.BaseDirectory, "e2e-result.json");
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        using var assets = E2EAssets.Create();
        try
        {
            return AppBuilder
                .Configure(() => new E2EApp(assets.Root, resultPath))
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Directory.Delete(assets.Root, recursive: true);
        }
    }
}

internal sealed class E2EAssets : IDisposable
{
    private E2EAssets(string root) => Root = root;

    public string Root { get; }

    public static E2EAssets Create()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "e2e-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), IndexHtml);
        return new E2EAssets(root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private const string IndexHtml = """
        <!doctype html>
        <html>
        <head><meta charset="utf-8"><title>Bridge E2E</title></head>
        <body>
        <div id="root">bridge-e2e</div>
        <script>
        (() => {
          const state = { hostApi: null, error: null };
          window.__e2e = state;
          const send = (type, payload) => {
            try {
              window.invokeCSharpAction(JSON.stringify({ type, payload }));
            } catch (error) {
              state.error = String(error);
            }
          };

          localStorage.setItem("e2e-key", "present");
          window.addEventListener("avalonia-react:message", event => {
            const message = event.detail;
            if (message.type === "avalonia:invoke:result") {
              state.hostApi = message.payload;
            }
            if (message.type === "avalonia:react:invoke") {
              const request = message.payload;
              send("avalonia:react:result", {
                requestId: request.requestId,
                result: "react:" + request.payload
              });
            }
          });

          send("avalonia:ready", { frontend: "e2e" });
          send("avalonia:invoke", {
            requestId: "host-api-1",
            api: "E2E",
            method: "getGreeting",
            args: ["webview"]
          });
        })();
        </script>
        </body>
        </html>
        """;
}

internal sealed class E2EApp(string assetsRoot, string resultPath) : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new E2EWindow(desktop, assetsRoot, resultPath);
            desktop.MainWindow = window;
            window.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class E2EWindow : Window
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly string _resultPath;
    private readonly AvaloniaReactWebView _webView;
    private readonly int _expectedPort;
    private readonly TaskCompletionSource _bridgeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public E2EWindow(IClassicDesktopStyleApplicationLifetime lifetime, string assetsRoot, string resultPath)
    {
        _lifetime = lifetime;
        _resultPath = resultPath;
        Title = "AvaloniaWebview.React WebView E2E";
        Width = 900;
        Height = 600;

        _expectedPort = GetFreePort();
        _webView = new AvaloniaReactWebView
        {
            SourceMode = ReactSourceMode.EmbeddedAssets,
            EmbeddedAssetsRoot = assetsRoot,
            EmbeddedAssetsPort = _expectedPort,
        };
        _webView.RegisterApi("E2E", new Dictionary<string, ReactApiMethodHandler>
        {
            ["getGreeting"] = context =>
            {
                var name = context.Arguments.Count > 0 ? context.Arguments[0].GetString() : "anonymous";
                return Task.FromResult<object?>($"Hello {name}");
            },
        });
        _webView.BridgeReady += (_, _) => _bridgeReady.TrySetResult();
        _webView.LoadStateChanged += (_, args) =>
        {
            if (args.State == ReactLoadState.Failed)
            {
                _bridgeReady.TrySetException(new InvalidOperationException(args.Error ?? "WebView load failed."));
            }
        };

        Content = _webView;
        Opened += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        var startedAt = System.Diagnostics.Stopwatch.StartNew();
        var result = new E2EResult();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _bridgeReady.Task.WaitAsync(timeout.Token);
            result.BridgeReady = true;
            result.Port = _webView.Source?.Port;
            result.FixedPort = result.Port == _expectedPort;

            result.HostApi = await ReadHostApiResultAsync(timeout.Token);
            result.HostToReact = await _webView.InvokeReactAsync<string>(
                "e2e:echo",
                "hello",
                TimeSpan.FromSeconds(5),
                timeout.Token);

            var cleanup = await _webView.ClearBrowserDataAsync(
                ReactWebViewDataKind.LocalStorage,
                timeout.Token);
            result.StorageCleanupSucceeded = cleanup.IsSuccess;
            result.StorageCleared = await ReadJavaScriptValueAsync(
                "localStorage.getItem('e2e-key')") is null;

            result.Success =
                result.BridgeReady &&
                result.FixedPort &&
                result.HostApi == "Hello webview" &&
                result.HostToReact == "react:hello" &&
                result.StorageCleanupSucceeded &&
                result.StorageCleared;
        }
        catch (Exception exception)
        {
            result.Success = false;
            result.Error = exception.ToString();
        }
        finally
        {
            result.DurationMilliseconds = startedAt.ElapsedMilliseconds;
            File.WriteAllText(
                _resultPath,
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            _webView.Shutdown();
            _lifetime.Shutdown(result.Success ? 0 : 1);
        }
    }

    private async Task<string?> ReadHostApiResultAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await ReadJavaScriptValueAsync("JSON.stringify(window.__e2e)");
            if (state is not null)
            {
                using var document = JsonDocument.Parse(state);
                var root = document.RootElement;
                if (root.TryGetProperty("hostApi", out var hostApi) &&
                    hostApi.ValueKind == JsonValueKind.Object &&
                    hostApi.TryGetProperty("result", out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                {
                    throw new InvalidOperationException(error.GetString());
                }
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException("The host API result did not arrive.");
    }

    private async Task<string?> ReadJavaScriptValueAsync(string script)
    {
        var raw = await _webView.InvokeScript(script);
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "null", StringComparison.Ordinal))
        {
            return null;
        }

        if (raw[0] == '"')
        {
            return JsonSerializer.Deserialize<string>(raw);
        }

        return raw;
    }

    private static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed record E2EResult
    {
        public bool Success { get; set; }
        public bool BridgeReady { get; set; }
        public int? Port { get; set; }
        public bool FixedPort { get; set; }
        public string? HostApi { get; set; }
        public string? HostToReact { get; set; }
        public bool StorageCleanupSucceeded { get; set; }
        public bool StorageCleared { get; set; }
        public string? Error { get; set; }
        public long DurationMilliseconds { get; set; }
    }
}
