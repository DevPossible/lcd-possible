using LCDPossible.Core.Monitoring;
using LCDPossible.Core.Plugins;
using LCDPossible.Core.Rendering;
using LCDPossible.Plugins.Core.Monitoring;
using LCDPossible.Plugins.Core.Panels;
using Microsoft.Extensions.Logging;

namespace LCDPossible.Plugins.Core;

/// <summary>
/// Core plugin providing system information panels.
/// Uses cross-platform hardware monitoring (LibreHardwareMonitor on Windows,
/// /sys+/proc on Linux, sysctl on macOS).
/// </summary>
public sealed class CorePlugin : IPanelPlugin
{
    private ILogger? _logger;
    private HardwareMonitorProvider? _hardwareProvider;

    public string PluginId => "lcdpossible.core";
    public string DisplayName => "LCDPossible Core Panels";
    public Version Version => new(1, 0, 0);
    public string Author => "LCDPossible Team";
    public Version MinimumSdkVersion => new(1, 0, 0);

    public IReadOnlyDictionary<string, PanelTypeInfo> PanelTypes { get; } = new Dictionary<string, PanelTypeInfo>
    {
        ["cpu-info"] = new PanelTypeInfo
        {
            TypeId = "cpu-info",
            DisplayName = "CPU Info",
            Description = "Detailed CPU information",
            Category = "System",
            IsLive = true
        },
        ["cpu-usage-text"] = new PanelTypeInfo
        {
            TypeId = "cpu-usage-text",
            DisplayName = "CPU Usage Text",
            Description = "CPU usage as large text",
            Category = "System",
            IsLive = true
        },
        ["cpu-usage-graphic"] = new PanelTypeInfo
        {
            TypeId = "cpu-usage-graphic",
            DisplayName = "CPU Usage Graphic",
            Description = "CPU usage with graphical bars",
            Category = "System",
            IsLive = true
        },
        ["ram-info"] = new PanelTypeInfo
        {
            TypeId = "ram-info",
            DisplayName = "RAM Info",
            Description = "Memory information",
            Category = "System",
            IsLive = true
        },
        ["ram-usage-text"] = new PanelTypeInfo
        {
            TypeId = "ram-usage-text",
            DisplayName = "RAM Usage Text",
            Description = "RAM usage as large text",
            Category = "System",
            IsLive = true
        },
        ["ram-usage-graphic"] = new PanelTypeInfo
        {
            TypeId = "ram-usage-graphic",
            DisplayName = "RAM Usage Graphic",
            Description = "RAM usage with graphical bar",
            Category = "System",
            IsLive = true
        },
        ["gpu-info"] = new PanelTypeInfo
        {
            TypeId = "gpu-info",
            DisplayName = "GPU Info",
            Description = "GPU information",
            Category = "System",
            IsLive = true
        },
        ["gpu-usage-text"] = new PanelTypeInfo
        {
            TypeId = "gpu-usage-text",
            DisplayName = "GPU Usage Text",
            Description = "GPU usage as large text",
            Category = "System",
            IsLive = true
        },
        ["gpu-usage-graphic"] = new PanelTypeInfo
        {
            TypeId = "gpu-usage-graphic",
            DisplayName = "GPU Usage Graphic",
            Description = "GPU usage with graphical bars",
            Category = "System",
            IsLive = true
        },
        ["basic-info"] = new PanelTypeInfo
        {
            TypeId = "basic-info",
            DisplayName = "Basic Info",
            Description = "Basic system information",
            Category = "System",
            IsLive = true
        },
        ["basic-usage-text"] = new PanelTypeInfo
        {
            TypeId = "basic-usage-text",
            DisplayName = "Basic Usage Text",
            Description = "Simple usage summary",
            Category = "System",
            IsLive = true
        },
        ["cpu-thermal-graphic"] = new PanelTypeInfo
        {
            TypeId = "cpu-thermal-graphic",
            DisplayName = "CPU Thermal",
            Description = "CPU temperature with graphical gauge",
            Category = "Thermal",
            IsLive = true
        },
        ["gpu-thermal-graphic"] = new PanelTypeInfo
        {
            TypeId = "gpu-thermal-graphic",
            DisplayName = "GPU Thermal",
            Description = "GPU temperature with graphical gauge",
            Category = "Thermal",
            IsLive = true
        },
        ["system-thermal-graphic"] = new PanelTypeInfo
        {
            TypeId = "system-thermal-graphic",
            DisplayName = "System Thermal",
            Description = "Combined CPU and GPU temperature display",
            Category = "Thermal",
            IsLive = true
        },
        ["network-info"] = new PanelTypeInfo
        {
            TypeId = "network-info",
            DisplayName = "Network Info",
            Description = "Network interfaces with IP, gateway, speed",
            Category = "Network",
            IsLive = true
        },
        ["cpu-status"] = new PanelTypeInfo
        {
            TypeId = "cpu-status",
            DisplayName = "CPU Status",
            Description = "CPU status with usage, temperature, frequency, and core info",
            Category = "System",
            IsLive = true
        },
["new-components-demo"] = new PanelTypeInfo
        {
            TypeId = "new-components-demo",
            DisplayName = "New Components Demo",
            Description = "Demo panel for ECharts and DaisyUI components",
            Category = "Demo",
            IsLive = true
        },
        ["cpu-usage-debug"] = new PanelTypeInfo
        {
            TypeId = "cpu-usage-debug",
            DisplayName = "CPU Usage Debug",
            Description = "Debug panel for Linux CPU usage calculation",
            Category = "Debug",
            IsLive = true
        }
    };

    public async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        _logger = context.CreateLogger("CorePlugin");

        // Initialize our own hardware monitoring provider
        _hardwareProvider = new HardwareMonitorProvider();
        await _hardwareProvider.InitializeAsync(cancellationToken);

        _logger.LogInformation("Core plugin initialized with hardware provider: {Provider} (Available: {Available})",
            _hardwareProvider.Name, _hardwareProvider.IsAvailable);
    }

    public IDisplayPanel? CreatePanel(string panelTypeId, PanelCreationContext context)
    {
        // Use our own hardware provider instead of the one from context
        var provider = _hardwareProvider ?? context.SystemProvider;

        if (provider == null)
        {
            _logger?.LogWarning("Cannot create {PanelType}: no system provider available", panelTypeId);
            return null;
        }

        IDisplayPanel? panel = panelTypeId.ToLowerInvariant() switch
        {
            "cpu-info" => new CpuInfoPanel(provider),
            "cpu-usage-text" => new CpuUsageTextPanel(provider),
            "cpu-usage-graphic" => new CpuUsageGraphicPanel(provider),
            "cpu-status" => new CpuStatusPanel(provider),
            "ram-info" => new RamInfoPanel(provider),
            "ram-usage-text" => new RamUsageTextPanel(provider),
            "ram-usage-graphic" => new RamUsageGraphicPanel(provider),
            "gpu-info" => new GpuInfoPanel(provider),
            "gpu-usage-text" => new GpuUsageTextPanel(provider),
            "gpu-usage-graphic" => new GpuUsageGraphicPanel(provider),
            "basic-info" => new BasicInfoPanel(provider),
            "basic-usage-text" => new BasicUsageTextPanel(provider),
            "cpu-thermal-graphic" => new CpuThermalGraphicPanel(provider),
            "gpu-thermal-graphic" => new GpuThermalGraphicPanel(provider),
            "system-thermal-graphic" => new SystemThermalGraphicPanel(provider),
"network-info" => new NetworkWidgetPanel(),
            "new-components-demo" => new NewComponentsDemoPanel(provider),
            "cpu-usage-debug" => new CpuUsageDebugPanel(),
            _ => null
        };

        // Set color scheme and services for all BasePanel-derived panels
        if (panel != null && panel is LCDPossible.Sdk.BasePanel basePanel)
        {
            if (context.ColorScheme != null)
            {
                basePanel.SetColorScheme(context.ColorScheme);
            }
            if (context.Services != null)
            {
                basePanel.SetServices(context.Services);
            }
        }

        return panel;
    }

    public void Dispose()
    {
        _hardwareProvider?.Dispose();
        _hardwareProvider = null;
        _logger?.LogInformation("Core plugin disposed");
    }
}
