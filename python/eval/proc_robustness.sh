#!/bin/sh
# Robustness of candidate procedural demo towns: each (level, mapSeed) x 10 run seeds, real detector.
cd "$(dirname "$0")/../.."
OUT=$1; shift; mkdir -p $OUT
for spec in "$@"; do
  L=${spec%%:*}; M=${spec##*:}
  for s in 2000001 2000003 2000005 2000007 2000009 2000011 2000013 2000015 2000017 2000019; do
    nice -n 5 Builds/Mission/Mission.x86_64 -batchmode -missions 1 -level $L -mapSeed $M -seedBase $s -detector cnn -segModel ${SEG:-unet_v4} \
      -navModel nav_v1 -landModel land_v3 -out $PWD/$OUT/L${L}_m${M}_s$s.csv -logFile /dev/null > /dev/null 2>&1
  done
done
