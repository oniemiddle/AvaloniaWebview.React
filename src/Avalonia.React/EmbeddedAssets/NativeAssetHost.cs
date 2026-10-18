using System.Runtime.InteropServices;
using Avalonia.Platform;

#if WINDOWS

#pragma warning disable CA1416 // This file is compiled only for Windows target frameworks.

namespace Avalonia.React;

// Windows-only optimization: map the published asset folder to a WebView2 virtual host so the
// WebView can load it without a loopback HTTP server. The cross-platform build still uses the
// loopback transport, which also provides SPA route fallback.
internal sealed class NativeAssetHost : IAssetTransport
{
    private const int DenyCors = 2;
    private const int SetVirtualHostNameToFolderMappingSlot = 71;

    // ICoreWebView2_3 IID (WebView2.h). Querying it first guarantees that the runtime object
    // actually implements the interface whose vtable layout is used below.
    private static readonly Guid CoreWebView2_3Iid = new("A0D6DF20-3B92-416D-AA0C-437A9C727857");

    private readonly string _hostName;
    private readonly string _rootPath;

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

        var coreWebView2 = webView2Handle.CoreWebView2;
        if (coreWebView2 == IntPtr.Zero)
        {
            return false;
        }

        var iid = CoreWebView2_3Iid;
        var queryResult = Marshal.QueryInterface(coreWebView2, in iid, out var webView3);
        if (queryResult < 0 || webView3 == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return TrySetVirtualHostNameToFolderMapping(webView3, _hostName, _rootPath);
        }
        finally
        {
            Marshal.Release(webView3);
        }
    }

    // ICoreWebView2_3 vtable, 0-based:
    //   0-2   IUnknown (QueryInterface, AddRef, Release)
    //   3-60  ICoreWebView2 (58 methods)
    //   61-67 ICoreWebView2_2 (7 methods)
    //   68    TrySuspend
    //   69    Resume
    //   70    get_IsSuspended
    //   71    SetVirtualHostNameToFolderMapping   <-- used here
    //   72    ClearVirtualHostNameToFolderMapping
    // Source: Microsoft.Web.WebView2 build/native/include/WebView2.h. The interface version is
    // fixed by the WebView2 contract, so the slot cannot move within a supported runtime.
    private static unsafe bool TrySetVirtualHostNameToFolderMapping(IntPtr webView3, string hostName, string folderPath)
    {
        var vtable = *(IntPtr**)webView3;
        if (vtable is null)
        {
            return false;
        }

        var functionPointer = vtable[SetVirtualHostNameToFolderMappingSlot];
        if (functionPointer == IntPtr.Zero)
        {
            return false;
        }

        var hostPointer = Marshal.StringToCoTaskMemUni(hostName);
        var folderPointer = Marshal.StringToCoTaskMemUni(folderPath);
        try
        {
            if (hostPointer == IntPtr.Zero || folderPointer == IntPtr.Zero)
            {
                return false;
            }

            // STDMETHODCALLTYPE: stdcall on x86, the platform default on x64.
            var setMapping = (delegate* unmanaged[Stdcall]<IntPtr, char*, char*, int, int>)functionPointer;
            return setMapping(webView3, (char*)hostPointer, (char*)folderPointer, DenyCors) >= 0;
        }
        finally
        {
            if (hostPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(hostPointer);
            }

            if (folderPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(folderPointer);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        // The mapping belongs to the CoreWebView2 instance and is released with it.
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

#endif
