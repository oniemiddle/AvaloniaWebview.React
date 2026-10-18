namespace AvaloniaWebview.React;

internal interface IAssetTransport : IAsyncDisposable
{
    Uri BaseUri { get; }
}
