using Avalonia.Platform;
using Avalonia.React;

namespace Avalonia.React.Tests;

public sealed class ReactAdapterDiagnosticsTests
{
    [Fact]
    public void Returns_null_for_an_installed_and_supported_adapter()
    {
        var info = new DetailedWebViewAdapterInfo(default, default, "1.0", true, true, string.Empty, default);

        Assert.Null(ReactAdapterDiagnostics.GetUnavailableReason(info));
    }

    [Fact]
    public void Returns_the_platform_reason_when_installed_but_unsupported()
    {
        var info = new DetailedWebViewAdapterInfo(default, default, "1.0", false, true, "This embedding scenario is not supported.", default);

        Assert.Equal("This embedding scenario is not supported.", ReactAdapterDiagnostics.GetUnavailableReason(info));
    }

    [Fact]
    public void Returns_a_friendly_message_when_the_runtime_is_missing()
    {
        var info = new DetailedWebViewAdapterInfo(default, default, string.Empty, false, false, string.Empty, default);

        Assert.Contains("runtime is not installed", ReactAdapterDiagnostics.GetUnavailableReason(info));
    }

    [Fact]
    public void Returns_a_fallback_for_an_unsupported_adapter_without_a_reason()
    {
        var info = new DetailedWebViewAdapterInfo(default, default, "1.0", false, true, string.Empty, default);

        Assert.Contains("does not support", ReactAdapterDiagnostics.GetUnavailableReason(info));
    }

    [Fact]
    public void Returns_null_when_diagnostics_are_unavailable()
    {
        Assert.Null(ReactAdapterDiagnostics.GetUnavailableReason(null));
    }
}
