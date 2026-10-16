#!/bin/sh
# usage: python/rl/train.sh <config.yaml> <run-id> <build-name> <num-envs> <arenas> [seed] [extra mlagents args...]
# RL is CPU-bound (SPEC Section 10): CUDA hidden, torch threads capped so parallel runs don't fight.
set -e
cd "$(dirname "$0")/../.."
CFG=$1; RUN=$2; BUILD=$3; NENV=$4; ARENAS=$5; SEED=${6:-1}
shift 6 2>/dev/null || shift $#
mkdir -p results/runs
export CUDA_VISIBLE_DEVICES="" OMP_NUM_THREADS=${OMP_NUM_THREADS:-4} MKL_NUM_THREADS=${OMP_NUM_THREADS:-4}
exec nice -n 5 .venv/bin/mlagents-learn "$CFG" --env "Builds/$BUILD/$BUILD.x86_64" --no-graphics \
  --num-envs "$NENV" --run-id "$RUN" --results-dir results/runs --seed "$SEED" --env-args -arenas "$ARENAS" "$@"
