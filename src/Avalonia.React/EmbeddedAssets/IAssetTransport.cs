namespace Avalonia.React;

internal interface IAssetTransport : IAsyncDisposable
{
    Uri BaseUri { get; }
}
