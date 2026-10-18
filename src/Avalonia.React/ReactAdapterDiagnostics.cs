using Avalonia.Platform;

namespace Avalonia.React;

/// <summary>
/// Converts adapter diagnostics into a message suitable for the loading/error overlay.
/// </summary>
internal static class ReactAdapterDiagnostics
{
    public static string? GetUnavailableReason(DetailedWebViewAdapterInfo? info)
    {
        if (info is null or { IsInstalled: true, IsSupported: true })
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(info.UnavailableReason))
        {
            return info.UnavailableReason;
        }

        return info.IsInstalled
            ? "The installed WebView engine does not support this hosting scenario."
            : "The WebView runtime is not installed. Install the platform runtime and restart the application.";
    }
}
