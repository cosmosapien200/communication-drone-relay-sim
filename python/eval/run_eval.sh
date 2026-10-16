#!/bin/sh
# usage: python/eval/run_eval.sh <out.csv> [EvalRunner args...]   (headless, logs next to the CSV)
cd "$(dirname "$0")/../.."
OUT=$1; shift
mkdir -p "$(dirname "$OUT")"
exec nice -n 5 Builds/Eval/Eval.x86_64 -batchmode -nographics -out "$OUT" -logFile "${OUT%.csv}.log" "$@" > /dev/null 2>&1
