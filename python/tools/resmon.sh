#!/bin/sh
# Logs load, memory, GPU and CPU temperature every 30 s to results/resources.log.
# Thermal guard: at >= 98 C, pause (SIGSTOP) training/eval processes for 45 s, then resume.
cd "$(dirname "$0")/../.."
while true; do
  T=$(cat /sys/class/thermal/thermal_zone*/temp 2>/dev/null | sort -n | tail -1)
  echo "$(date +%H:%M:%S) load=$(cut -d' ' -f1-3 /proc/loadavg) mem_avail=$(awk '/MemAvailable/{printf "%.1fG",$2/1048576}' /proc/meminfo) swap_used=$(awk '/SwapTotal/{t=$2}/SwapFree/{printf "%.1fG",(t-$2)/1048576}' /proc/meminfo) gpu=$(nvidia-smi --query-gpu=utilization.gpu,memory.used,temperature.gpu --format=csv,noheader 2>/dev/null | tr -d ' ') cpu_temp=$T" >> results/resources.log
  if [ "${T:-0}" -ge 98000 ]; then
    echo "$(date +%H:%M:%S) THERMAL GUARD: pausing training for 45 s" >> results/resources.log
    pkill -STOP -f "mlagents-learn|Training.x86_64|Eval.x86_64|train_seg.py"
    sleep 45
    pkill -CONT -f "mlagents-learn|Training.x86_64|Eval.x86_64|train_seg.py"
  fi
  sleep 30
done
