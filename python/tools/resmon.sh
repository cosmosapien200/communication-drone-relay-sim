#!/bin/sh
# Logs system load, memory and GPU every 60 s to results/resources.log (keeps an eye on the machine during training).
cd "$(dirname "$0")/../.."
while true; do
  echo "$(date +%H:%M:%S) load=$(cut -d' ' -f1-3 /proc/loadavg) mem_avail=$(awk '/MemAvailable/{printf "%.1fG",$2/1048576}' /proc/meminfo) swap_used=$(awk '/SwapTotal/{t=$2}/SwapFree/{printf "%.1fG",(t-$2)/1048576}' /proc/meminfo) gpu=$(nvidia-smi --query-gpu=utilization.gpu,memory.used,temperature.gpu --format=csv,noheader 2>/dev/null | tr -d ' ') cpu_temp=$(cat /sys/class/thermal/thermal_zone*/temp 2>/dev/null | sort -n | tail -1)" >> results/resources.log
  sleep 60
done
