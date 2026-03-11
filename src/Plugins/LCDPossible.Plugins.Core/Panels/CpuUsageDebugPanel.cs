using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using LCDPossible.Core.Monitoring;
using LCDPossible.Sdk;

namespace LCDPossible.Plugins.Core.Panels;

/// <summary>
/// Debug panel to show CPU usage calculation details on Linux.
/// </summary>
public sealed class CpuUsageDebugPanel : WidgetPanel
{
    public override string PanelId => "cpu-usage-debug";
    public override string DisplayName => "CPU Usage Debug";

    protected override async Task<object> GetPanelDataAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsLinux())
        {
            return await GetLinuxDebugDataAsync(ct);
        }
        
        return new { error = "This panel is for Linux only" };
    }

    private async Task<object> GetLinuxDebugDataAsync(CancellationToken ct)
    {
        try
        {
            var data = new Dictionary<string, object>();

            // Read /proc/stat
            if (File.Exists("/proc/stat"))
            {
                var statLines = await File.ReadAllLinesAsync("/proc/stat", ct);
                var cpuLine = statLines.FirstOrDefault(l => l.StartsWith("cpu "));
                
                if (cpuLine != null)
                {
                    data["cpuLine"] = cpuLine;
                    
                    var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5)
                    {
                        // Parse jiffies
                        var user = long.Parse(parts[1], CultureInfo.InvariantCulture);
                        var nice = long.Parse(parts[2], CultureInfo.InvariantCulture);
                        var system = long.Parse(parts[3], CultureInfo.InvariantCulture);
                        var idle = long.Parse(parts[4], CultureInfo.InvariantCulture);
                        var iowait = parts.Length > 5 ? long.Parse(parts[5], CultureInfo.InvariantCulture) : 0;
                        
                        var total = user + nice + system + idle + iowait;
                        var idleTotal = idle + iowait;
                        var active = total - idleTotal;
                        
                        data["user"] = user;
                        data["nice"] = nice;
                        data["system"] = system;
                        data["idle"] = idle;
                        data["iowait"] = iowait;
                        data["total"] = total;
                        data["idleTotal"] = idleTotal;
                        data["active"] = active;
                        
                        // Simulate calculation with previous values (if we had them)
                        data["usageIfDelta"] = total > 0 ? (100.0 * active / total).ToString("F2") + "%" : "0%";
                    }
                }
            }
            
            // Test with top command as alternative
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "top",
                        Arguments = "-bn1 | grep '%Cpu(s):'",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);
                
                if (!string.IsNullOrEmpty(output))
                {
                    data["topOutput"] = output.Trim();
                    
                    // Parse top output: %Cpu(s):  1.2 us,  0.5 sy,  0.0 ni, 98.3 id,  0.0 wa,  0.0 hi,  0.0 si,  0.0 st
                    var topMatch = Regex.Match(output, @"%Cpu\(s\):\s+(\d+\.?\d*)\s+us,\s+(\d+\.?\d*)\s+sy");
                    if (topMatch.Success)
                    {
                        var us = float.Parse(topMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                        var sy = float.Parse(topMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                        data["topUsage"] = (us + sy).ToString("F2") + "%";
                    }
                }
            }
            catch (Exception ex)
            {
                data["topError"] = ex.Message;
            }
            
            return data;
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    protected override IEnumerable<WidgetDefinition> DefineWidgets(object panelData)
    {
        dynamic data = panelData;
        
        if (data.error != null)
        {
            yield return new WidgetDefinition("lcd-stat-card", 12, 1, new {
                title = "Error",
                value = data.error,
                status = "error"
            });
            yield break;
        }

        // Show /proc/stat line
        if (data.cpuLine != null)
        {
            yield return new WidgetDefinition("lcd-info-list", 12, 2, new {
                items = new[] {
                    new { label = "/proc/stat", value = data.cpuLine, color = "primary" }
                }
            });
        }

        // Show jiffies breakdown
        if (data.total != null)
        {
            yield return new WidgetDefinition("lcd-info-list", 12, 3, new {
                items = new[] {
                    new { label = "User", value = data.user.ToString(), color = "info" },
                    new { label = "Nice", value = data.nice.ToString(), color = "info" },
                    new { label = "System", value = data.system.ToString(), color = "info" },
                    new { label = "Idle", value = data.idle.ToString(), color = "success" },
                    new { label = "IOwait", value = data.iowait.ToString(), color = "warning" },
                    new { label = "Total", value = data.total.ToString(), color = "primary" },
                    new { label = "Active", value = data.active.ToString(), color = "accent" },
                    new { label = "Usage (if delta)", value = data.usageIfDelta, color = "primary" }
                }
            });
        }

        // Show top command output
        if (data.topOutput != null)
        {
            yield return new WidgetDefinition("lcd-info-list", 12, 2, new {
                items = new[] {
                    new { label = "top output", value = data.topOutput, color = "primary" },
                    new { label = "top usage", value = data.topUsage, color = "accent" }
                }
            });
        }

        if (data.topError != null)
        {
            yield return new WidgetDefinition("lcd-stat-card", 12, 1, new {
                title = "top error",
                value = data.topError,
                status = "warning"
            });
        }
    }
}