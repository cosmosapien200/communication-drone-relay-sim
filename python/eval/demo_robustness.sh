#!/bin/sh
# Per-map robustness: each demo map x 10 wind/noise seeds with the real U-Net, for a given mission player build.
# usage: python/eval/demo_robustness.sh <BuildDir> <outdir>
cd "$(dirname "$0")/../.."
B=$1; OUT=$2; mkdir -p $OUT
for i in 0 1 2 3 4 5; do
  for s in 2000001 2000003 2000005 2000007 2000009 2000011 2000013 2000015 2000017 2000019; do
    nice -n 5 Builds/$B/Mission.x86_64 -batchmode -missions 1 -demoMap $i -runSeed $s -detector cnn -segModel ${SEG:-unet_v2} \
      -navModel nav_v1 -landModel land_v3 -dumpDir $PWD/$OUT/dump_m${i}_s$s -out $PWD/$OUT/m${i}_s$s.csv -logFile /dev/null > /dev/null 2>&1
  done
done
