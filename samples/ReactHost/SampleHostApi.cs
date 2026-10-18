using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using AvaloniaWebview.React;
using ReactHost.Frontend;

namespace ReactHost;

/// <summary>
/// Host API implementation for the sample. Registered through the generated
/// <see cref="HostApiBridge"/> helper, so the bridge stays reflection-free and trimming/AOT-safe.
/// </summary>
public sealed class SampleHostApi : IHostApi
{
    // Monotonic uptime: immune to wall-clock changes while the UI polls every two seconds.
    private static readonly Stopwatch ProcessUptime = Stopwatch.StartNew();
    private static readonly string AvaloniaVersion = ResolveAvaloniaVersion();

    public Task<string> GetGreetingAsync(string name) =>
        Task.FromResult($"Hello {name}, from the generated .NET API!");

    public Task<RuntimeSnapshot> GetRuntimeSnapshotAsync()
    {
        var availability = ReactWebViewAvailability.Check();
        return Task.FromResult(new RuntimeSnapshot
        {
            Host = "Avalonia 12",
            WebView = availability.IsAvailable
                ? $"{availability.Engine} {availability.RuntimeVersion}".Trim()
                : availability.Message,
            Health = availability.IsAvailable ? ServiceHealth.Healthy : ServiceHealth.Degraded,
            Tags = ["shared-session", "aot-json", "host-api"],
        });
    }

    /// <summary>
    /// Real cross-platform metrics for the React dashboard. Only BCL APIs are used (no reflection,
    /// no P/Invoke), so the sample keeps working under trimming and Native AOT.
    /// </summary>
    public Task<RuntimeMetrics> GetRuntimeMetricsAsync()
    {
        var availability = ReactWebViewAvailability.Check();
        var memory = GC.GetGCMemoryInfo();

        return Task.FromResult(new RuntimeMetrics
        {
            Os = $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}",
            Runtime = RuntimeInformation.FrameworkDescription,
            Avalonia = AvaloniaVersion,
            WebView = availability.IsAvailable
                ? $"{availability.Engine} {availability.RuntimeVersion}".Trim()
                : availability.Message,
            Machine = Environment.MachineName,
            ProcessorCount = Environment.ProcessorCount,
            TotalMemoryBytes = memory.TotalAvailableMemoryBytes,
            WorkingSetBytes = Environment.WorkingSet,
            GcHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
            ThreadCount = CountThreads(),
            UptimeSeconds = ProcessUptime.Elapsed.TotalSeconds,
            CapturedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
        });
    }

    private static double CountThreads()
    {
        // Process.Threads is not implemented on macOS; the Windows guard keeps the sample portable.
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.GetCurrentProcess();
            return process.Threads.Count;
        }

        // Portable approximation: worker threads plus the main thread.
        return ThreadPool.ThreadCount + 1;
    }

    private static string ResolveAvaloniaVersion()
    {
        var version = typeof(Application).Assembly.GetName().Version;
        if (version is null)
        {
            return "Avalonia";
        }

        // Drop the assembly revision and keep the "12.1.0" style product version.
        return version.Build >= 0
            ? string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}")
            : $"{version.Major}.{version.Minor}";
    }
}
