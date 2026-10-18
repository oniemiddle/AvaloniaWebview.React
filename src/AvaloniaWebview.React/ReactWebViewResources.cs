namespace AvaloniaWebview.React;

/// <summary>Selects the browser data cleared by <see cref="AvaloniaReactWebView.ClearBrowserDataAsync"/>.</summary>
[Flags]
public enum ReactWebViewDataKind
{
    /// <summary>No browser data.</summary>
    None = 0,

    /// <summary>Cookies managed by the native WebView.</summary>
    Cookies = 1 << 0,

    /// <summary>The current origin's <c>localStorage</c>.</summary>
    LocalStorage = 1 << 1,

    /// <summary>The current origin's <c>sessionStorage</c>.</summary>
    SessionStorage = 1 << 2,

    /// <summary>The current origin's Cache Storage entries.</summary>
    CacheStorage = 1 << 3,

    /// <summary>The current origin's IndexedDB databases.</summary>
    IndexedDb = 1 << 4,

    /// <summary>Service worker registrations for the current origin.</summary>
    ServiceWorkers = 1 << 5,

    /// <summary>All script-visible browser storage.</summary>
    BrowserStorage = LocalStorage | SessionStorage | CacheStorage | IndexedDb | ServiceWorkers,

    /// <summary>Cookies and all script-visible browser storage.</summary>
    All = Cookies | BrowserStorage,
}

/// <summary>Result of <see cref="AvaloniaReactWebView.ClearBrowserDataAsync"/>.</summary>
public sealed record ReactWebViewCleanupResult
{
    /// <summary>Gets the data kinds that were requested.</summary>
    public required ReactWebViewDataKind Requested { get; init; }

    /// <summary>Gets the data kinds that were cleared successfully.</summary>
    public ReactWebViewDataKind Cleared { get; init; }

    /// <summary>Gets the data kinds that could not be cleared.</summary>
    public ReactWebViewDataKind Failed { get; init; }

    /// <summary>Gets diagnostic messages for partial failures or unsupported operations.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Gets whether every requested data kind was cleared.</summary>
    public bool IsSuccess => Failed == ReactWebViewDataKind.None;
}

/// <summary>Result of <see cref="ReactWebViewResources.ClearExtractedAssetsAsync"/>.</summary>
public sealed record ReactWebViewAssetCleanupResult
{
    /// <summary>Gets the extraction roots that were deleted.</summary>
    public IReadOnlyList<string> DeletedRoots { get; init; } = [];

    /// <summary>Gets the number of extracted files deleted.</summary>
    public int DeletedFiles { get; init; }

    /// <summary>Gets the approximate number of bytes deleted.</summary>
    public long DeletedBytes { get; init; }

    /// <summary>Gets deletion failures, if any.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Gets whether every existing extraction root was deleted.</summary>
    public bool IsSuccess => Errors.Count == 0;
}

/// <summary>
/// Reclaims resources owned by AvaloniaWebview.React that outlive individual WebView controls,
/// such as the temporary directory used for assembly-embedded frontend files.
/// </summary>
public static class ReactWebViewResources
{
    /// <summary>
    /// Deletes the temporary directories created by
    /// <see cref="AvaloniaReactWebView.UseAssemblyEmbeddedAssets"/> and clears the extraction cache.
    /// A custom <see cref="AssemblyEmbeddedAssetOptions.ExtractionRoot"/> is deleted too when it was
    /// registered by this process.
    /// </summary>
    /// <remarks>
    /// Call this after WebView instances that use assembly-embedded assets have been shut down.
    /// Active asset servers may still need those files. The method is safe to call repeatedly.
    /// </remarks>
    public static Task<ReactWebViewAssetCleanupResult> ClearExtractedAssetsAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ClearExtractedAssets(cancellationToken), cancellationToken);

    private static ReactWebViewAssetCleanupResult ClearExtractedAssets(CancellationToken cancellationToken)
    {
        var roots = AssemblyAssetExtractor.GetExtractionRoots()
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(root => root.Length)
            .ToArray();

        var deletedRoots = new List<string>();
        var errors = new List<string>();
        var deletedFiles = 0;
        long deletedBytes = 0;

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                AssemblyAssetExtractor.ForgetExtractionRoot(root);
                continue;
            }

            try
            {
                var files = 0;
                long bytes = 0;
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    files++;
                    try
                    {
                        bytes += new FileInfo(file).Length;
                    }
                    catch (Exception)
                    {
                        // The file can disappear while a concurrent cleanup is running.
                    }
                }

                Directory.Delete(root, recursive: true);
                deletedRoots.Add(root);
                deletedFiles += files;
                deletedBytes += bytes;
                AssemblyAssetExtractor.ForgetExtractionRoot(root);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                errors.Add($"{root}: {exception.Message}");
            }
        }

        AssemblyAssetExtractor.ClearCachedResults();

        // Remove the shared default parent when it is now empty; custom roots are left alone.
        try
        {
            var defaultBase = AssemblyAssetExtractor.GetDefaultExtractionBase();
            if (Directory.Exists(defaultBase) && !Directory.EnumerateFileSystemEntries(defaultBase).Any())
            {
                Directory.Delete(defaultBase);
            }
        }
        catch (Exception)
        {
            // Best-effort cleanup only.
        }

        return new ReactWebViewAssetCleanupResult
        {
            DeletedRoots = deletedRoots,
            DeletedFiles = deletedFiles,
            DeletedBytes = deletedBytes,
            Errors = errors,
        };
    }
}

/// <summary>Builds the script used to clear script-visible browser storage.</summary>
internal static class ReactBrowserDataCleanup
{
    public static string BuildScript(ReactWebViewDataKind kinds, string requestId)
    {
        var browserKinds = (int)(kinds & ReactWebViewDataKind.BrowserStorage);
        return $$"""
            (() => {
              const requestId = "{{requestId}}";
              const kinds = {{browserKinds}};
              const failures = [];
              const finish = () => {
                try {
                  if (typeof window.invokeCSharpAction === "function") {
                    window.invokeCSharpAction(JSON.stringify({
                      type: "avalonia:resources:cleared",
                      payload: { requestId, failures }
                    }));
                  }
                } catch (ignored) {
                }
              };

              (async () => {
                try {
                  if (kinds & 2) {
                    try { localStorage.clear(); } catch (error) { failures.push("localStorage: " + error); }
                  }
                  if (kinds & 4) {
                    try { sessionStorage.clear(); } catch (error) { failures.push("sessionStorage: " + error); }
                  }
                  if (kinds & 8) {
                    if (typeof caches === "undefined") {
                      failures.push("cacheStorage: unsupported");
                    } else {
                      try {
                        for (const key of await caches.keys()) {
                          if (!await caches.delete(key)) {
                            failures.push("cacheStorage: " + key);
                          }
                        }
                      } catch (error) {
                        failures.push("cacheStorage: " + error);
                      }
                    }
                  }
                  if (kinds & 16) {
                    if (!("indexedDB" in window) || typeof indexedDB.databases !== "function") {
                      failures.push("indexedDb: unsupported");
                    } else {
                      try {
                        for (const database of await indexedDB.databases()) {
                          if (!database.name) continue;
                          await new Promise(resolve => {
                            const request = indexedDB.deleteDatabase(database.name);
                            request.onsuccess = () => resolve();
                            request.onerror = () => { failures.push("indexedDb: delete failed for " + database.name); resolve(); };
                            request.onblocked = () => { failures.push("indexedDb: blocked for " + database.name); resolve(); };
                          });
                        }
                      } catch (error) {
                        failures.push("indexedDb: " + error);
                      }
                    }
                  }
                  if (kinds & 32) {
                    if (!navigator.serviceWorker) {
                      failures.push("serviceWorkers: unsupported");
                    } else {
                      try {
                        for (const registration of await navigator.serviceWorker.getRegistrations()) {
                          if (!await registration.unregister()) {
                            failures.push("serviceWorkers: unregister failed");
                          }
                        }
                      } catch (error) {
                        failures.push("serviceWorkers: " + error);
                      }
                    }
                  }
                } catch (error) {
                  failures.push("cleanup: " + error);
                } finally {
                  finish();
                }
              })();
            })();
            """;
    }
}
