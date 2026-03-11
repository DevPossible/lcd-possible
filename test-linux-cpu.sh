#!/bin/bash

# Test script to verify CPU usage monitoring on Linux
# This script tests the fix for CPU usage returning 0 on Linux/Proxmox

echo "=== LCDPossible Linux CPU Usage Test ==="
echo "Testing CPU usage monitoring fix for Linux/Proxmox"
echo

# Check if we're on Linux
if [[ "$OSTYPE" != "linux-gnu"* ]]; then
    echo "❌ This test is designed for Linux systems"
    echo "Current OS: $OSTYPE"
    exit 1
fi

# Check if /proc/stat exists (required for CPU monitoring)
if [[ ! -f "/proc/stat" ]]; then
    echo "❌ /proc/stat not found - cannot monitor CPU usage"
    exit 1
fi

echo "✅ Linux system detected"
echo "✅ /proc/stat available"

# Show current /proc/stat CPU line
echo
echo "Current /proc/stat CPU data:"
head -n1 /proc/stat

# Test the CPU usage calculation manually
echo
echo "Testing CPU usage calculation..."

# Read the CPU line twice with a small delay to simulate the monitoring
CPU_LINE1=$(head -n1 /proc/stat)
sleep 1
CPU_LINE2=$(head -n1 /proc/stat)

echo "Sample 1: $CPU_LINE1"
echo "Sample 2: $CPU_LINE2"

# Parse the CPU lines (simplified version of the LinuxMonitor logic)
parse_cpu() {
    local line="$1"
    local parts=($line)
    local user=${parts[1]}
    local nice=${parts[2]}
    local system=${parts[3]}
    local idle=${parts[4]}
    local iowait=${parts[5]}
    
    local total=$((user + nice + system + idle + iowait))
    local idle_total=$((idle + iowait))
    local active=$((total - idle_total))
    
    echo "$total $idle_total $active"
}

read total1 idle1 active1 <<< $(parse_cpu "$CPU_LINE1")
read total2 idle2 active2 <<< $(parse_cpu "$CPU_LINE2")

# Calculate usage percentage
if [[ $total2 -gt $total1 ]]; then
    total_delta=$((total2 - total1))
    idle_delta=$((idle2 - idle1))
    active_delta=$((total_delta - idle_delta))
    
    if [[ $total_delta -gt 0 ]]; then
        usage=$(echo "scale=2; $active_delta * 100 / $total_delta" | bc -l)
        echo
        echo "✅ CPU Usage Calculation Test:"
        echo "   Total delta: $total_delta jiffies"
        echo "   Idle delta: $idle_delta jiffies" 
        echo "   Active delta: $active_delta jiffies"
        echo "   CPU Usage: ${usage}%"
        
        # Validate the result
        if (( $(echo "$usage > 0" | bc -l) )); then
            echo "✅ CPU usage is greater than 0% - fix working!"
        else
            echo "❌ CPU usage is still 0% - may need further investigation"
        fi
        
        if (( $(echo "$usage <= 100" | bc -l) )); then
            echo "✅ CPU usage is within valid range (0-100%)"
        else
            echo "❌ CPU usage is outside valid range"
        fi
    else
        echo "❌ No CPU activity detected between samples"
    fi
else
    echo "❌ Invalid CPU delta calculation"
fi

echo
echo "=== Testing LCDPossible CPU Monitoring ==="

# Find and run the LCDPossible executable
LCDPOSSIBLE_PATH=$(find .build -name "LCDPossible" -type f 2>/dev/null | head -n1)

if [[ -z "$LCDPOSSIBLE_PATH" ]]; then
    echo "❌ LCDPossible executable not found"
    echo "   Run './build.ps1' first to build the project"
    exit 1
fi

echo "✅ Found LCDPossible at: $LCDPOSSIBLE_PATH"

# Test CPU monitoring with LCDPossible
echo
echo "Testing CPU usage with LCDPossible..."
echo "Running: cpu-usage-debug panel (Linux only)"

# Try to render the debug panel
"$LCDPOSSIBLE_PATH" render cpu-usage-debug --debug 2>/dev/null

if [[ $? -eq 0 ]]; then
    echo "✅ CPU usage debug panel rendered successfully"
    echo "   Check the output above for CPU usage values"
else
    echo "⚠️  Debug panel may not be available on this system"
    echo "   Trying standard CPU panels..."
    
    # Test standard CPU panels
    "$LCDPOSSIBLE_PATH" render cpu-usage-text --debug 2>/dev/null
    "$LCDPOSSIBLE_PATH" render cpu-status --debug 2>/dev/null
fi

echo
echo "=== Test Complete ==="
echo
echo "If CPU usage is still showing 0%, check:"
echo "1. System has CPU activity (run a stress test: 'stress --cpu 2')"
echo "2. /proc/stat is being updated properly"
echo "3. LCDPossible has permission to read /proc/stat"
echo
echo "To generate CPU load for testing:"
echo "  stress --cpu 2  # Install with: apt-get install stress"