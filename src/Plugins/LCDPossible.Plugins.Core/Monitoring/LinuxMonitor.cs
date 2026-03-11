using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using LCDPossible.Core.Monitoring;

namespace LCDPossible.Plugins.Core.Monitoring;

/// <summary>
/// Linux hardware monitor using /sys, /proc, and GPU driver CLIs.
/// Provides CPU info/usage/temp, RAM, and GPU data via nvidia-smi or rocm-smi.
/// </summary>
internal sealed partial class LinuxMonitor : IPlatformMonitor
{
    private string? _cpuName;
    private int _cpuCoreCount;
    private long _lastTotalJiffies;
    private long _lastIdleJiffies;
    private long[]? _lastCoreTotal;
    private long[]? _lastCoreIdle;
    private bool _hasNvidiaSmi;
    private bool _hasRocmSmi;
    private bool _hasAmdGpu;

    public string PlatformName => "Linux";
    public bool IsAvailable { get; private set; }

public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Get CPU info
            if (File.Exists("/proc/cpuinfo"))
            {
                var cpuInfo = await File.ReadAllTextAsync("/proc/cpuinfo", cancellationToken);
                var modelMatch = ModelNameRegex().Match(cpuInfo);
                if (modelMatch.Success)
                {
                    _cpuName = modelMatch.Groups[1].Value.Trim();
                }

                _cpuCoreCount = ProcessorCountRegex().Matches(cpuInfo).Count;
                if (_cpuCoreCount == 0) _cpuCoreCount = Environment.ProcessorCount;
            }

            // Initialize CPU usage tracking by taking first sample
            await InitializeCpuUsageTrackingAsync(cancellationToken);

            // Check for GPU tools
            _hasNvidiaSmi = CanRunCommand("nvidia-smi", "--version");
            _hasRocmSmi = CanRunCommand("rocm-smi", "--version");
            _hasAmdGpu = Directory.Exists("/sys/class/drm") &&
                         Directory.GetDirectories("/sys/class/drm", "card*").Any(d =>
                             File.Exists(Path.Combine(d, "device/vendor")) &&
                             File.ReadAllText(Path.Combine(d, "device/vendor")).Trim() == "0x1002");

            IsAvailable = true;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    private async Task InitializeCpuUsageTrackingAsync(CancellationToken cancellationToken)
    {
        // Take first sample of CPU stats to establish baseline for delta calculation
        if (File.Exists("/proc/stat"))
        {
            var statLines = await File.ReadAllLinesAsync("/proc/stat", cancellationToken);
            var cpuLine = statLines.FirstOrDefault(l => l.StartsWith("cpu "));

            if (cpuLine != null)
            {
                var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    var user = long.Parse(parts[1], CultureInfo.InvariantCulture);
                    var nice = long.Parse(parts[2], CultureInfo.InvariantCulture);
                    var system = long.Parse(parts[3], CultureInfo.InvariantCulture);
                    var idle = long.Parse(parts[4], CultureInfo.InvariantCulture);
                    var iowait = parts.Length > 5 ? long.Parse(parts[5], CultureInfo.InvariantCulture) : 0;

                    _lastTotalJiffies = user + nice + system + idle + iowait;
                    _lastIdleJiffies = idle + iowait;
                }
            }

            // Initialize per-core tracking
            var coreLines = statLines.Where(l => CpuCoreLineRegex().IsMatch(l)).ToList();
            if (coreLines.Count > 0)
            {
                _lastCoreTotal = new long[coreLines.Count];
                _lastCoreIdle = new long[coreLines.Count];

                for (int i = 0; i < coreLines.Count; i++)
                {
                    var parts = coreLines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5)
                    {
                        var user = long.Parse(parts[1], CultureInfo.InvariantCulture);
                        var nice = long.Parse(parts[2], CultureInfo.InvariantCulture);
                        var system = long.Parse(parts[3], CultureInfo.InvariantCulture);
                        var idle = long.Parse(parts[4], CultureInfo.InvariantCulture);
                        var iowait = parts.Length > 5 ? long.Parse(parts[5], CultureInfo.InvariantCulture) : 0;

                        _lastCoreTotal[i] = user + nice + system + idle + iowait;
                        _lastCoreIdle[i] = idle + iowait;
                    }
                }
            }
        }
    }

    public async Task<SystemMetrics?> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return null;
        }

        var metrics = new SystemMetrics
        {
            Cpu = await GetCpuMetricsAsync(cancellationToken),
            Memory = GetMemoryMetrics(),
            Gpu = await GetGpuMetricsAsync(cancellationToken),
            Timestamp = DateTime.UtcNow
        };

        return metrics;
    }

    private async Task<CpuMetrics> GetCpuMetricsAsync(CancellationToken cancellationToken)
    {
        var cpu = new CpuMetrics
        {
            Name = _cpuName ?? Environment.MachineName
        };

        // Read CPU usage from /proc/stat
        if (File.Exists("/proc/stat"))
        {
            var statLines = await File.ReadAllLinesAsync("/proc/stat", cancellationToken);
            var cpuLine = statLines.FirstOrDefault(l => l.StartsWith("cpu "));

            if (cpuLine != null)
            {
                var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    // user, nice, system, idle, iowait, irq, softirq, steal
                    var user = long.Parse(parts[1], CultureInfo.InvariantCulture);
                    var nice = long.Parse(parts[2], CultureInfo.InvariantCulture);
                    var system = long.Parse(parts[3], CultureInfo.InvariantCulture);
                    var idle = long.Parse(parts[4], CultureInfo.InvariantCulture);
                    var iowait = parts.Length > 5 ? long.Parse(parts[5], CultureInfo.InvariantCulture) : 0;

                    var total = user + nice + system + idle + iowait;
                    var idleTotal = idle + iowait;

if (_lastTotalJiffies > 0)
                    {
                        var totalDelta = total - _lastTotalJiffies;
                        var idleDelta = idleTotal - _lastIdleJiffies;

                        if (totalDelta > 0)
                        {
                            cpu.UsagePercent = (float)(100.0 * (totalDelta - idleDelta) / totalDelta);
                        }
                    }
                    else
                    {
                        // Fallback: use current snapshot as an estimate (not as accurate but better than 0)
                        if (total > 0)
                        {
                            cpu.UsagePercent = (float)(100.0 * (total - idleTotal) / total);
                        }
                    }

                    _lastTotalJiffies = total;
                    _lastIdleJiffies = idleTotal;
                }
            }

            // Per-core usage
            var coreLines = statLines.Where(l => CpuCoreLineRegex().IsMatch(l)).ToList();
            if (coreLines.Count > 0)
            {
                _lastCoreTotal ??= new long[coreLines.Count];
                _lastCoreIdle ??= new long[coreLines.Count];

                for (int i = 0; i < coreLines.Count; i++)
                {
                    var parts = coreLines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5)
                    {
                        var user = long.Parse(parts[1], CultureInfo.InvariantCulture);
                        var nice = long.Parse(parts[2], CultureInfo.InvariantCulture);
                        var system = long.Parse(parts[3], CultureInfo.InvariantCulture);
                        var idle = long.Parse(parts[4], CultureInfo.InvariantCulture);
                        var iowait = parts.Length > 5 ? long.Parse(parts[5], CultureInfo.InvariantCulture) : 0;

                        var total = user + nice + system + idle + iowait;
                        var idleTotal = idle + iowait;

if (_lastCoreTotal[i] > 0)
                        {
                            var totalDelta = total - _lastCoreTotal[i];
                            var idleDelta = idleTotal - _lastCoreIdle[i];

                            if (totalDelta > 0)
                            {
                                cpu.CoreUsages.Add((float)(100.0 * (totalDelta - idleDelta) / totalDelta));
                            }
                        }
                        else
                        {
                            // Fallback: use current snapshot as an estimate
                            if (total > 0)
                            {
                                cpu.CoreUsages.Add((float)(100.0 * (total - idleTotal) / total));
                            }
                        }

                        _lastCoreTotal[i] = total;
                        _lastCoreIdle[i] = idleTotal;
                    }
                }
            }
        }

        // Read CPU temperature
        cpu.TemperatureCelsius = GetCpuTemperature();

        // Read CPU frequency
        cpu.FrequencyMhz = GetCpuFrequency();

        return cpu;
    }

    private float? GetCpuTemperature()
    {
        // Try thermal zones first
        if (Directory.Exists("/sys/class/thermal"))
        {
            foreach (var zone in Directory.GetDirectories("/sys/class/thermal", "thermal_zone*"))
            {
                var typePath = Path.Combine(zone, "type");
                var tempPath = Path.Combine(zone, "temp");

                if (File.Exists(typePath) && File.Exists(tempPath))
                {
                    var type = File.ReadAllText(typePath).Trim().ToLowerInvariant();

                    // Look for CPU-related thermal zones
                    if (type.Contains("x86_pkg_temp") || type.Contains("coretemp") ||
                        type.Contains("cpu") || type.Contains("package"))
                    {
                        var tempStr = File.ReadAllText(tempPath).Trim();
                        if (int.TryParse(tempStr, out int milliCelsius))
                        {
                            return milliCelsius / 1000.0f;
                        }
                    }
                }
            }
        }

        // Try hwmon as fallback
        if (Directory.Exists("/sys/class/hwmon"))
        {
            foreach (var hwmon in Directory.GetDirectories("/sys/class/hwmon"))
            {
                var namePath = Path.Combine(hwmon, "name");
                if (File.Exists(namePath))
                {
                    var name = File.ReadAllText(namePath).Trim().ToLowerInvariant();
                    if (name.Contains("coretemp") || name.Contains("k10temp") || name.Contains("zenpower"))
                    {
                        // Look for temp1_input or similar
                        foreach (var tempFile in Directory.GetFiles(hwmon, "temp*_input"))
                        {
                            var tempStr = File.ReadAllText(tempFile).Trim();
                            if (int.TryParse(tempStr, out int milliCelsius))
                            {
                                return milliCelsius / 1000.0f;
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    private float? GetCpuFrequency()
    {
        // Try to read current frequency
        var freqPath = "/sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq";
        if (File.Exists(freqPath))
        {
            var freqStr = File.ReadAllText(freqPath).Trim();
            if (long.TryParse(freqStr, out long khz))
            {
                return khz / 1000.0f; // Convert kHz to MHz
            }
        }

        return null;
    }

    private MemoryMetrics GetMemoryMetrics()
    {
        var memory = new MemoryMetrics();

        if (File.Exists("/proc/meminfo"))
        {
            var memInfo = File.ReadAllText("/proc/meminfo");

            var totalMatch = MemTotalRegex().Match(memInfo);
            var availableMatch = MemAvailableRegex().Match(memInfo);
            var freeMatch = MemFreeRegex().Match(memInfo);

            if (totalMatch.Success)
            {
                var totalKb = long.Parse(totalMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                memory.TotalGb = totalKb / (1024f * 1024f);
            }

            if (availableMatch.Success)
            {
                var availableKb = long.Parse(availableMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                memory.AvailableGb = availableKb / (1024f * 1024f);
            }
            else if (freeMatch.Success)
            {
                // Fallback to MemFree if MemAvailable isn't present
                var freeKb = long.Parse(freeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                memory.AvailableGb = freeKb / (1024f * 1024f);
            }

            memory.UsedGb = memory.TotalGb - memory.AvailableGb;

            if (memory.TotalGb > 0)
            {
                memory.UsagePercent = (memory.UsedGb / memory.TotalGb) * 100f;
            }
        }

        return memory;
    }

    private async Task<GpuMetrics> GetGpuMetricsAsync(CancellationToken cancellationToken)
    {
        // Try NVIDIA first
        if (_hasNvidiaSmi)
        {
            var nvidia = await GetNvidiaGpuMetricsAsync(cancellationToken);
            if (nvidia != null) return nvidia;
        }

        // Try AMD
        if (_hasRocmSmi)
        {
            var amd = await GetAmdRocmGpuMetricsAsync(cancellationToken);
            if (amd != null) return amd;
        }

        // Try AMD via sysfs
        if (_hasAmdGpu)
        {
            var amd = GetAmdSysfsGpuMetrics();
            if (amd != null) return amd;
        }

        return new GpuMetrics { Name = "N/A", UsagePercent = 0 };
    }

    private async Task<GpuMetrics?> GetNvidiaGpuMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var output = await RunCommandAsync("nvidia-smi",
                "--query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total,power.draw,fan.speed,clocks.gr,clocks.mem " +
                "--format=csv,noheader,nounits",
                cancellationToken);

            if (string.IsNullOrEmpty(output)) return null;

            var parts = output.Split(',').Select(p => p.Trim()).ToArray();
            if (parts.Length < 5) return null;

            var gpu = new GpuMetrics
            {
                Name = parts[0]
            };

            if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var usage))
                gpu.UsagePercent = usage;
            if (float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
                gpu.TemperatureCelsius = temp;
            if (float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var memUsed))
                gpu.MemoryUsedMb = memUsed;
            if (float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var memTotal))
                gpu.MemoryTotalMb = memTotal;

            if (parts.Length > 5 && float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var power))
                gpu.PowerWatts = power;
            if (parts.Length > 6 && float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var fan))
                gpu.FanSpeedPercent = fan;
            if (parts.Length > 7 && float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var coreClock))
                gpu.CoreClockMhz = coreClock;
            if (parts.Length > 8 && float.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out var memClock))
                gpu.MemoryClockMhz = memClock;

            if (gpu.MemoryTotalMb > 0 && gpu.MemoryUsedMb.HasValue)
            {
                gpu.MemoryUsagePercent = (gpu.MemoryUsedMb / gpu.MemoryTotalMb) * 100f;
            }

            return gpu;
        }
        catch
        {
            return null;
        }
    }

    private async Task<GpuMetrics?> GetAmdRocmGpuMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            // rocm-smi output parsing
            var output = await RunCommandAsync("rocm-smi", "--showuse --showtemp --showmeminfo vram --showpower --showfan", cancellationToken);
            if (string.IsNullOrEmpty(output)) return null;

            var gpu = new GpuMetrics { Name = "AMD GPU" };

            // Parse GPU utilization
            var useMatch = GpuUseRegex().Match(output);
            if (useMatch.Success && float.TryParse(useMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var usage))
                gpu.UsagePercent = usage;

            // Parse temperature
            var tempMatch = EdgeTempRegex().Match(output);
            if (tempMatch.Success && float.TryParse(tempMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
                gpu.TemperatureCelsius = temp;

            // Parse power
            var powerMatch = SocketPowerRegex().Match(output);
            if (powerMatch.Success && float.TryParse(powerMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var power))
                gpu.PowerWatts = power;

            return gpu;
        }
        catch
        {
            return null;
        }
    }

    private GpuMetrics? GetAmdSysfsGpuMetrics()
    {
        try
        {
            var cardDirs = Directory.GetDirectories("/sys/class/drm", "card*")
                .Where(d => !d.Contains("-"))
                .ToList();

            foreach (var cardDir in cardDirs)
            {
                var vendorPath = Path.Combine(cardDir, "device/vendor");
                if (!File.Exists(vendorPath)) continue;

                var vendor = File.ReadAllText(vendorPath).Trim();
                if (vendor != "0x1002") continue; // AMD vendor ID

                var gpu = new GpuMetrics { Name = "AMD GPU" };

                // Try to get GPU name
                var namePath = Path.Combine(cardDir, "device/product_name");
                if (File.Exists(namePath))
                    gpu.Name = File.ReadAllText(namePath).Trim();

                // GPU busy percent
                var busyPath = Path.Combine(cardDir, "device/gpu_busy_percent");
                if (File.Exists(busyPath) && float.TryParse(File.ReadAllText(busyPath).Trim(), out var busy))
                    gpu.UsagePercent = busy;

                // Temperature (hwmon)
                var hwmonDirs = Directory.GetDirectories(Path.Combine(cardDir, "device/hwmon"));
                foreach (var hwmon in hwmonDirs)
                {
                    var temp1Path = Path.Combine(hwmon, "temp1_input");
                    if (File.Exists(temp1Path) && int.TryParse(File.ReadAllText(temp1Path).Trim(), out var milliTemp))
                    {
                        gpu.TemperatureCelsius = milliTemp / 1000f;
                        break;
                    }
                }

                return gpu;
            }
        }
        catch
        {
            // Ignore errors reading sysfs
        }

        return null;
    }

    private static bool CanRunCommand(string command, string args)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(1000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> RunCommandAsync(string command, string args, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return output.Trim();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        // No resources to dispose
    }

    // Regex patterns
    [GeneratedRegex(@"model name\s*:\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex ModelNameRegex();

    [GeneratedRegex(@"^processor\s*:", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ProcessorCountRegex();

    [GeneratedRegex(@"^cpu\d+\s", RegexOptions.Multiline)]
    private static partial Regex CpuCoreLineRegex();

    [GeneratedRegex(@"MemTotal:\s*(\d+)\s*kB", RegexOptions.IgnoreCase)]
    private static partial Regex MemTotalRegex();

    [GeneratedRegex(@"MemAvailable:\s*(\d+)\s*kB", RegexOptions.IgnoreCase)]
    private static partial Regex MemAvailableRegex();

    [GeneratedRegex(@"MemFree:\s*(\d+)\s*kB", RegexOptions.IgnoreCase)]
    private static partial Regex MemFreeRegex();

    [GeneratedRegex(@"GPU use\s*\(%\)\s*:\s*(\d+\.?\d*)")]
    private static partial Regex GpuUseRegex();

    [GeneratedRegex(@"Temperature \(Sensor edge\)\s*\(C\)\s*:\s*(\d+\.?\d*)")]
    private static partial Regex EdgeTempRegex();

    [GeneratedRegex(@"Average Socket Power\s*\(W\)\s*:\s*(\d+\.?\d*)")]
    private static partial Regex SocketPowerRegex();
}
