namespace AvaloniaWebview.React.Tests;

public sealed class ReactWebViewSessionTests
{
    [Fact]
    public void Shared_session_is_a_singleton()
    {
        Assert.Same(ReactWebViewSession.Shared, ReactWebViewSession.Shared);
        Assert.Null(ReactWebViewSession.Shared.ProfileName);
    }

    [Fact]
    public void CreateEphemeral_sets_platform_ephemeral_flags()
    {
        var first = ReactWebViewSession.CreateEphemeral();
        var second = ReactWebViewSession.CreateEphemeral();

        Assert.NotSame(first, second);
        Assert.True(first.InPrivateMode);
        Assert.True(first.NonPersistentDataStore);
        Assert.True(first.EphemeralDataManager);
        Assert.NotSame(ReactWebViewSession.Shared, first);
    }
}
