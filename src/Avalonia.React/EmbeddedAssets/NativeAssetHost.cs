using System.Runtime.InteropServices;
using Avalonia.Platform;

#pragma warning disable CA1416 // The type is used only after an OperatingSystem.IsWindows() check.

namespace Avalonia.React;

// WebView2 does not raise WebResourceRequested for virtual-host requests, so route fallback is
// intentionally left to the loopback transport. This transport only maps the asset folder.
internal sealed class NativeAssetHost : IAssetTransport
{
    private const int DenyCors = 2;

    private readonly string _hostName;
    private readonly string _rootPath;
    private ICoreWebView2_3? _webView;

    public NativeAssetHost(EmbeddedAssetOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("NativeHost asset embedding is only supported on Windows.");
        }

        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));
        if (!Directory.Exists(_rootPath))
        {
            throw new DirectoryNotFoundException($"Embedded asset root '{_rootPath}' does not exist.");
        }

        var fallback = options.SpaFallback;
        if (string.IsNullOrWhiteSpace(fallback) ||
            Path.IsPathRooted(fallback) ||
            fallback.Split('/', '\\').Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("SpaFallback must be a relative file name inside the asset root.", nameof(options));
        }

        if (!File.Exists(Path.Combine(_rootPath, fallback)))
        {
            throw new FileNotFoundException($"Embedded asset root '{_rootPath}' does not contain '{fallback}'.", fallback);
        }

        if (!IsValidHostName(options.NativeHostName))
        {
            throw new ArgumentException("NativeHostName must be a valid host name, for example 'avalonia-react.local'.", nameof(options));
        }

        _hostName = options.NativeHostName;
        BaseUri = new Uri($"https://{_hostName}/{fallback}");
    }

    public Uri BaseUri { get; }

    public bool TryApply(IPlatformHandle? handle)
    {
        if (!OperatingSystem.IsWindows() || handle is not IWindowsWebView2PlatformHandle webView2Handle)
        {
            return false;
        }

        var coreWebView2 = IntPtr.Zero;
        try
        {
            coreWebView2 = webView2Handle.CoreWebView2;
            if (coreWebView2 == IntPtr.Zero)
            {
                return false;
            }

            var webView = (ICoreWebView2_3)Marshal.GetObjectForIUnknown(coreWebView2);
            if (webView.SetVirtualHostNameToFolderMapping(_hostName, _rootPath, DenyCors) < 0)
            {
                Marshal.ReleaseComObject(webView);
                return false;
            }

            _webView = webView;
            return true;
        }
        catch (Exception exception) when (
            exception is COMException or InvalidCastException or PlatformNotSupportedException or ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            if (coreWebView2 != IntPtr.Zero)
            {
                Marshal.Release(coreWebView2);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        var webView = _webView;
        _webView = null;
        if (webView is not null)
        {
            try
            {
                Marshal.ReleaseComObject(webView);
            }
            catch (Exception)
            {
            }
        }

        return ValueTask.CompletedTask;
    }

    private static bool IsValidHostName(string hostName) =>
        !string.IsNullOrWhiteSpace(hostName) &&
        hostName.Length <= 255 &&
        hostName.Contains('.') &&
        !hostName.StartsWith('.') &&
        !hostName.EndsWith('.') &&
        hostName.All(character => char.IsLetterOrDigit(character) || character is '-' or '.');
}
