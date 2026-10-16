#!/bin/sh
# Landing experiment (SPEC Section 12): RL vs PID visual servoing at 0/low/medium/high gusts (+ beyond training),
# measured detector noise, 100 held-out episodes each; plus full-quadrotor transfer and doubled detector noise.
cd "$(dirname "$0")/../.."
OUT=results/eval/land
R=python/eval/run_eval.sh
for G in 0 1.5 3 4.5 6; do
  W=$(python3 -c "print($G/3)")
  $R $PWD/$OUT/v3_g${G}.csv  -eval land -controller policy -model land_v3 -gust $G -windMean $W -episodes 100 -seedBase 3000001
  $R $PWD/$OUT/v2_g${G}.csv  -eval land -controller policy -model land_v2 -gust $G -windMean $W -episodes 100 -seedBase 3000001
  $R $PWD/$OUT/pid_g${G}.csv -eval land -controller pid -gust $G -windMean $W -episodes 100 -seedBase 3000001
done
for C in policy pid; do
  $R $PWD/$OUT/full_${C}_g3.csv -eval land -controller $C -model land_v3 -gust 3 -windMean 1 -episodes 100 -seedBase 3000001 -flight full
  $R $PWD/$OUT/noise2_${C}_g3.csv -eval land -controller $C -model land_v3 -gust 3 -windMean 1 -episodes 100 -seedBase 3000001 -noiseScale 2
done
