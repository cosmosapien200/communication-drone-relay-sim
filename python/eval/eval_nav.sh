#!/bin/sh
# Navigation experiment (SPEC Section 12): policy vs A*/potential/straight, levels 1-4, 100 held-out episodes each,
# plus fast->full flight-mode transfer for the policy and A*.
cd "$(dirname "$0")/../.."
MODEL=${1:-nav_v1}; OUT=results/eval/nav_$MODEL
for L in 1 2 3 4; do
  for C in policy astar potential straight; do
    python/eval/run_eval.sh $PWD/$OUT/${C}_L${L}_fast.csv -eval nav -controller $C -model $MODEL -level $L -episodes 100 -seedBase 1000001
  done
done
for L in 2 4; do
  for C in policy astar; do
    python/eval/run_eval.sh $PWD/$OUT/${C}_L${L}_full.csv -eval nav -controller $C -model $MODEL -level $L -episodes 100 -seedBase 1000001 -flight full
  done
done
