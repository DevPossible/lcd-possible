# Linux CPU Usage Fix Summary

## Problem
CPU usage on Linux (including Proxmox) was returning 0% because the CPU usage calculation requires delta values between two samples, but the first call had no previous data to compare against.

## Root Cause
The `LinuxMonitor.cs` implementation calculates CPU usage by comparing jiffies (CPU time ticks) between two calls to `/proc/stat`. On the first call:
- `_lastTotalJiffies` and `_lastIdleJiffies` are 0
- The calculation is skipped entirely
- CPU usage returns 0%

## Solution Implemented

### 1. Enhanced LinuxMonitor.cs
- **Added `InitializeCpuUsageTrackingAsync()`**: Takes initial CPU sample during initialization
- **Added fallback calculation**: When no delta is available, use current snapshot as estimate
- **Fixed per-core usage**: Same fallback applied to individual CPU cores
- **Made initialization async**: Proper async file reading

### 2. Enhanced HardwareMonitorProvider.cs  
- **Added warmup call**: For Linux, takes an initial reading and waits 100ms
- **Ensures meaningful delta**: First real call will have proper delta calculation

### 3. Added Debug Panel
- **Created `CpuUsageDebugPanel`**: Shows raw `/proc/stat` data and calculations
- **Helps diagnose issues**: Displays jiffies values and intermediate calculations
- **Linux-specific**: Only works on Linux systems

## Files Modified

1. `src/Plugins/LCDPossible.Plugins.Core/Monitoring/LinuxMonitor.cs`
   - Added `InitializeCpuUsageTrackingAsync()` method
   - Added fallback calculations for CPU and per-core usage
   - Made initialization async

2. `src/Plugins/LCDPossible.Plugins.Core/Monitoring/HardwareMonitorProvider.cs`
   - Added warmup call for Linux monitor
   - Small delay to ensure meaningful delta

3. `src/Plugins/LCDPossible.Plugins.Core/Panels/CpuUsageDebugPanel.cs` (new)
   - Debug panel to show CPU calculation details
   - Shows `/proc/stat` parsing and jiffies values

4. `src/Plugins/LCDPossible.Plugins.Core/CorePlugin.cs`
   - Registered debug panel

## Testing

### Manual Testing
```bash
# Build the project
./build.ps1

# Test CPU monitoring on Linux
./test-linux-cpu.sh

# Render debug panel
./start-app.ps1 render cpu-usage-debug --debug

# Test standard CPU panels
./start-app.ps1 render cpu-usage-text --debug
./start-app.ps1 render cpu-status --debug
```

### Generate CPU Load for Testing
```bash
# Install stress tool
sudo apt-get install stress

# Generate CPU load
stress --cpu 2

# Monitor with LCDPossible
./start-app.ps1 render cpu-usage-text --debug
```

## Expected Behavior

### Before Fix
- First CPU usage reading: 0%
- Subsequent readings: Correct values (after delta available)
- Per-core usage: 0% on first call

### After Fix  
- First CPU usage reading: Estimated value (current snapshot)
- Subsequent readings: Accurate delta-based values
- Per-core usage: Estimated on first call, accurate thereafter
- Debug panel: Shows calculation details for troubleshooting

## Technical Details

### CPU Usage Calculation
```csharp
// Delta-based calculation (accurate)
cpu.UsagePercent = (float)(100.0 * (totalDelta - idleDelta) / totalDelta);

// Fallback calculation (estimate)  
cpu.UsagePercent = (float)(100.0 * (total - idleTotal) / total);
```

### Jiffies from /proc/stat
```
cpu  user  nice  system  idle  iowait  irq  softirq  steal
cpu 12345 678   23456   7890  1234    0    0        0
```

- **Total jiffies**: user + nice + system + idle + iowait
- **Idle jiffies**: idle + iowait  
- **Active jiffies**: total - idle
- **Usage %**: (active / total) * 100

## Platform-Specific Notes

### Linux/Proxmox
- Uses `/proc/stat` for CPU usage
- Uses `/sys/class/thermal/` and `/sys/class/hwmon/` for temperature
- Requires proper file permissions (usually OK for user processes)

### Proxmox Specific
- Same as Linux since Proxmox is Debian-based
- Container CPU usage may appear different than host
- CPU load from VMs/containers will be reflected in host stats

## Troubleshooting

If CPU usage is still 0% after the fix:

1. **Check /proc/stat permissions**:
   ```bash
   ls -la /proc/stat
   cat /proc/stat | head -n1
   ```

2. **Generate CPU load**:
   ```bash
   stress --cpu 2
   # Monitor in real-time
   watch "head -n1 /proc/stat"
   ```

3. **Use debug panel**:
   ```bash
   ./start-app.ps1 render cpu-usage-debug --debug
   ```

4. **Check system activity**:
   ```bash
   top
   htop
   mpstat
   ```

## Future Improvements

1. **Configurable warmup delay**: Allow customization of initialization delay
2. **Alternative CPU sources**: Consider `sysstat` or `procps` tools as fallbacks
3. **Container-aware metrics**: Better handling for containerized environments
4. **CPU frequency scaling**: Account for dynamic frequency changes in usage calculation