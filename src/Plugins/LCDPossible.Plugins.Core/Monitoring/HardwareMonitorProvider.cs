using System.Runtime.InteropServices;
using LCDPossible.Core.Monitoring;

namespace LCDPossible.Plugins.Core.Monitoring;

/// <summary>
/// Cross-platform hardware monitoring provider.
/// Uses the best available source on each platform:
/// - Windows: LibreHardwareMonitor (full data)
/// - Linux: /sys and /proc filesystem (CPU temp, usage, RAM)
/// - macOS: sysctl and IOKit (CPU/RAM usage, limited temp)
/// </summary>
public sealed class HardwareMonitorProvider : ISystemInfoProvider
{
    private readonly IPlatformMonitor _platformMonitor;
    private bool _disposed;

    public string Name => $"HardwareMonitor ({_platformMonitor.PlatformName})";
    public bool IsAvailable { get; private set; }

    public HardwareMonitorProvider()
    {
        _platformMonitor = CreatePlatformMonitor();
    }

    private static IPlatformMonitor CreatePlatformMonitor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsMonitor();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxMonitor();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsMonitor();
        }
        else
        {
            return new FallbackMonitor();
        }
    }

public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _platformMonitor.InitializeAsync(cancellationToken);
            IsAvailable = _platformMonitor.IsAvailable;

            // For Linux, take a warmup reading to establish CPU usage baseline
            if (IsAvailable && _platformMonitor is LinuxMonitor)
            {
                await _platformMonitor.GetMetricsAsync(cancellationToken);
                // Small delay to ensure we get a meaningful delta on the first real call
                await Task.Delay(100, cancellationToken);
            }
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public async Task<SystemMetrics?> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || _disposed)
        {
            return null;
        }

        return await _platformMonitor.GetMetricsAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _platformMonitor.Dispose();
    }
}

/// <summary>
/// Interface for platform-specific monitoring implementations.
/// </summary>
internal interface IPlatformMonitor : IDisposable
{
    string PlatformName { get; }
    bool IsAvailable { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<SystemMetrics?> GetMetricsAsync(CancellationToken cancellationToken = default);
}
