using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace AvaloniaWebview.React;

/// <summary>Identifies the desktop platform hosting the React WebView.</summary>
public enum ReactWebViewPlatform
{
    /// <summary>The host platform could not be identified.</summary>
    Unknown = 0,
    /// <summary>Microsoft Windows.</summary>
    Windows = 1,
    /// <summary>Apple macOS.</summary>
    MacOS = 2,
    /// <summary>Linux.</summary>
    Linux = 3,
    /// <summary>Android (not supported by the desktop hosting model).</summary>
    Android = 4,
    /// <summary>WebAssembly browser (not supported by the desktop hosting model).</summary>
    Browser = 5,
}

/// <summary>Describes whether the native WebView runtime can host a React page.</summary>
public enum ReactWebViewAvailabilityStatus
{
    /// <summary>A supported WebView runtime is installed and usable.</summary>
    Available = 0,
    /// <summary>The platform is supported but its WebView runtime is missing.</summary>
    RuntimeMissing = 1,
    /// <summary>The host platform is not supported for desktop hosting.</summary>
    UnsupportedPlatform = 2,
    /// <summary>Headless WebView mode is active, so there is no visible host.</summary>
    UnsupportedHost = 3,
    /// <summary>The platform probe failed before a conclusion could be reached.</summary>
    ProbeFailed = 4,
}

/// <summary>Result of <see cref="ReactWebViewAvailability.Check"/>.</summary>
public sealed record ReactWebViewAvailabilityReport
{
    /// <summary>Gets the detected host platform.</summary>
    public required ReactWebViewPlatform Platform { get; init; }

    /// <summary>Gets whether a native WebView can host the React page.</summary>
    public required bool IsAvailable { get; init; }

    /// <summary>Gets the detailed availability status.</summary>
    public required ReactWebViewAvailabilityStatus Status { get; init; }

    /// <summary>Gets the detected Avalonia adapter type, when probing reached it.</summary>
    public WebViewAdapterType? AdapterType { get; init; }

    /// <summary>Gets the detected WebView engine name, such as Blink or WebKit.</summary>
    public string? Engine { get; init; }

    /// <summary>Gets the detected runtime version, when the platform exposes it.</summary>
    public string? RuntimeVersion { get; init; }

    /// <summary>Gets a human-readable summary of the probe result.</summary>
    public required string Message { get; init; }

    /// <summary>Gets install or remediation guidance when the runtime is not available.</summary>
    public string? Guidance { get; init; }

    /// <summary>Gets the platform's reason for marking the adapter unavailable.</summary>
    public string? UnavailableReason { get; init; }

    /// <summary>Gets the embedding scenarios supported by the detected adapter.</summary>
    public string? SupportedScenarios { get; init; }

    /// <summary>Gets diagnostic details useful in logs and crash reports.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary>Returns a compact one-line form of the report.</summary>
    public override string ToString() =>
        $"{Status}: {Message}" + (Guidance is null ? string.Empty : $" {Guidance}");
}

/// <summary>
/// Static preflight checks that run before an Avalonia WebView is created, so startup code can
/// fail fast with actionable platform/runtime guidance.
/// </summary>
public static class ReactWebViewAvailability
{
    private const string WebView2InstallerUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>Probes the current platform and WebView runtime without creating a WebView.</summary>
    public static ReactWebViewAvailabilityReport Check()
    {
        var details = new List<string>
        {
            "OS: " + RuntimeInformation.OSDescription,
            "Process architecture: " + RuntimeInformation.ProcessArchitecture,
            "Avalonia WebView assembly: " + typeof(NativeWebView).Assembly.GetName().Version,
        };

        var (platform, adapterType) = DetectPlatform();
        if (adapterType is null)
        {
            return CreateReport(
                platform,
                ReactWebViewAvailabilityStatus.UnsupportedPlatform,
                "AvaloniaWebview.React supports Windows, macOS, and Linux desktop hosts.",
                "Run the application on Windows 10/11, macOS, or Linux.",
                details);
        }

        details.Add("Adapter: " + adapterType);

        DetailedWebViewAdapterInfo? info;
        try
        {
            info = WebViewAdapterInfo.GetAdapterInfo(adapterType.Value);
        }
        catch (Exception exception) when (IsMissingRuntimeException(exception))
        {
            details.Add("Runtime load failed: " + exception);
            return CreateReport(
                platform,
                ReactWebViewAvailabilityStatus.RuntimeMissing,
                "The platform WebView runtime could not be loaded: " + exception.Message,
                GetInstallGuidance(platform),
                details);
        }
        catch (Exception exception)
        {
            details.Add("Adapter probe failed: " + exception);
            return CreateReport(
                platform,
                ReactWebViewAvailabilityStatus.ProbeFailed,
                "The platform WebView adapter could not be probed: " + exception.Message,
                GetTroubleshootingGuidance(platform),
                details);
        }

        if (info is null)
        {
            details.Add("Adapter probe returned no information.");
            return CreateReport(
                platform,
                ReactWebViewAvailabilityStatus.UnsupportedPlatform,
                "The current platform does not expose a WebView adapter for AvaloniaWebview.React.",
                GetTroubleshootingGuidance(platform),
                details);
        }

        details.Add("Engine: " + info.Engine);
        details.Add("Runtime version: " + (string.IsNullOrWhiteSpace(info.Version) ? "(unknown)" : info.Version));
        details.Add("Supported scenarios: " + info.SupportedScenarios);
        if (!string.IsNullOrWhiteSpace(info.UnavailableReason))
        {
            details.Add("Unavailable reason: " + info.UnavailableReason);
        }

        if (!info.IsInstalled)
        {
            return CreateReport(
                platform,
                ReactWebViewAvailabilityStatus.RuntimeMissing,
                string.IsNullOrWhiteSpace(info.UnavailableReason)
                    ? "The platform WebView runtime is not installed."
                    : info.UnavailableReason!,
                GetInstallGuidance(platform),
                details,
                info);
        }

        if (!info.IsSupported)
        {
            return CreateReport(
                platform,
                ReactWebViewAvailabilityStatus.UnsupportedPlatform,
                string.IsNullOrWhiteSpace(info.UnavailableReason)
                    ? "The installed WebView runtime does not support this hosting scenario."
                    : info.UnavailableReason!,
                GetTroubleshootingGuidance(platform),
                details,
                info);
        }

        var version = string.IsNullOrWhiteSpace(info.Version) ? string.Empty : " " + info.Version;
        return CreateReport(
            platform,
            ReactWebViewAvailabilityStatus.Available,
            $"{info.Engine} WebView runtime{version} is available.",
            null,
            details,
            info);
    }

    /// <summary>
    /// Probes the runtime and throws <see cref="PlatformNotSupportedException"/> with install
    /// guidance when no usable WebView is available.
    /// </summary>
    public static ReactWebViewAvailabilityReport EnsureAvailable()
    {
        var report = Check();
        if (report.IsAvailable)
        {
            return report;
        }

        throw new PlatformNotSupportedException(report.ToString());
    }

    private static ReactWebViewAvailabilityReport CreateReport(
        ReactWebViewPlatform platform,
        ReactWebViewAvailabilityStatus status,
        string message,
        string? guidance,
        List<string> details,
        DetailedWebViewAdapterInfo? info = null) =>
        new()
        {
            Platform = platform,
            IsAvailable = status == ReactWebViewAvailabilityStatus.Available,
            Status = status,
            AdapterType = info?.Type,
            Engine = info?.Engine.ToString(),
            RuntimeVersion = string.IsNullOrWhiteSpace(info?.Version) ? null : info.Version,
            Message = message,
            Guidance = guidance,
            UnavailableReason = string.IsNullOrWhiteSpace(info?.UnavailableReason) ? null : info.UnavailableReason,
            SupportedScenarios = info is null ? null : info.SupportedScenarios.ToString(),
            Details = details,
        };

    private static (ReactWebViewPlatform Platform, WebViewAdapterType? AdapterType) DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return (ReactWebViewPlatform.Windows, WebViewAdapterType.WebView2);
        }

        if (OperatingSystem.IsMacOS())
        {
            return (ReactWebViewPlatform.MacOS, WebViewAdapterType.WkWebView);
        }

        if (OperatingSystem.IsLinux())
        {
            return (ReactWebViewPlatform.Linux, WebViewAdapterType.WebKitGtk);
        }

        if (OperatingSystem.IsAndroid())
        {
            return (ReactWebViewPlatform.Android, WebViewAdapterType.AndroidWebView);
        }

        if (OperatingSystem.IsBrowser())
        {
            return (ReactWebViewPlatform.Browser, WebViewAdapterType.BrowserIFrame);
        }

        return (ReactWebViewPlatform.Unknown, null);
    }

    private static bool IsMissingRuntimeException(Exception exception) =>
        exception is DllNotFoundException or EntryPointNotFoundException or FileNotFoundException ||
        exception is TypeInitializationException { InnerException: { } inner } && IsMissingRuntimeException(inner);

    private static string? GetInstallGuidance(ReactWebViewPlatform platform) =>
        platform switch
        {
            ReactWebViewPlatform.Windows =>
                $"Install the Microsoft Edge WebView2 Evergreen Runtime: {WebView2InstallerUrl}",
            ReactWebViewPlatform.Linux =>
                "Install WebKitGTK 4.1 (Ubuntu/Debian: 'sudo apt install libwebkit2gtk-4.1-0'; Fedora: 'sudo dnf install webkit2gtk4.1').",
            ReactWebViewPlatform.MacOS =>
                "Update macOS so the system WKWebView runtime is available.",
            _ => null,
        };

    private static string GetTroubleshootingGuidance(ReactWebViewPlatform platform) =>
        platform switch
        {
            ReactWebViewPlatform.Windows =>
                $"Install or repair the Microsoft Edge WebView2 Evergreen Runtime: {WebView2InstallerUrl}",
            ReactWebViewPlatform.Linux =>
                "Install WebKitGTK 4.1 and make sure the display/GTK session is available.",
            ReactWebViewPlatform.MacOS =>
                "Update macOS; WKWebView ships with the operating system.",
            _ => "Use a supported Windows, macOS, or Linux desktop host.",
        };
}
