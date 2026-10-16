#!/bin/sh
# Full-mission experiments (SPEC Section 12) on held-out seeds (odd, 2000001+), 100 missions per condition.
# usage: python/eval/eval_missions.sh [missions] [segModel] [weakModel]
cd "$(dirname "$0")/../.."
N=${1:-100}; SEG=${2:-unet_v2}; WEAK=${3:-unet_weak}; OUT=$PWD/results/eval/missions
M="nice -n 5 Builds/Mission/Mission.x86_64 -batchmode -missions $N -seedBase 2000001 -navModel ${NAV:-nav_v2} -landModel ${LAND:-land_v3}"
run() { name=$1; shift; $M -out $OUT/$name.csv -logFile $OUT/$name.log "$@" > /dev/null 2>&1; echo "done $name"; }
mkdir -p $OUT
# 1. vision ablation: ground truth vs real detector vs weakened detector (rendered, CNN in the loop)
run vision_gt     -nographics -level 2 -detector gt
run vision_cnn    -level 2 -detector cnn -segModel $SEG
run vision_weak   -level 2 -detector cnn -segModel $WEAK
run vision_mid    -level 2 -detector cnn -segModel unet_mid
# 2. detector-quality sweep with the measured noise model (headline plot)
for k in 0 0.5 1 2 4 8; do run quality_k$k -nographics -level 2 -detector noisy -noiseScale $k; done
# 3. shared map vs none
run shared_on  -nographics -level 2 -detector noisy -shared 1
run shared_off -nographics -level 2 -detector noisy -shared 0
# 4. spot-selection strategy
for s in best random nearest; do run strategy_$s -nographics -level 2 -detector noisy -strategy $s; done
# 5. difficulty
for L in 3 4; do run level_L$L -nographics -level $L -detector noisy; done
# 6. RL vs scripted stand-ins (A*-free reactive follower / PID landing)
run standin_nav  -nographics -level 2 -detector noisy -scriptedNav 1
run standin_land -nographics -level 2 -detector noisy -scriptedLand 1
# 7. full quadrotor flight mode
run flight_full -nographics -level 2 -detector noisy -flight full
