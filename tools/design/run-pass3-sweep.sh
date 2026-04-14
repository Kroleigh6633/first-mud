#!/usr/bin/env bash
# Pass #3 encounter-balance sweep — 5 zones × biome monster pool × scaled vs unscaled.
# Invocations are deterministic (seed=42). Results captured as JSON in sim-logs/.
set -e
cd "$(dirname "$0")/FirstMud.DesignTools"

declare -A zones=(
  [portmere]=1
  [starting-road]=2
  [thornwood]=3
  [drowned-coast]=5
  [ashen-reach]=8
)

declare -A pools=(
  [portmere]="cave-rat stray-dog highway-bandit wild-horse rogue-knight pack-alpha-wolf wandering-ogre mounted-raider"
  [starting-road]="cave-rat stray-dog highway-bandit wild-horse rogue-knight pack-alpha-wolf wandering-ogre mounted-raider"
  [thornwood]="timber-wolf wild-boar thornweaver-spider forest-bandit dire-bear treant elder-stag thornwood-guardian"
  [drowned-coast]="giant-crab mud-skipper tide-lurker reef-shark sea-serpent kraken-spawn deep-horror drowned-revenant"
  [ashen-reach]="sand-scorpion dust-viper fire-lizard giant-centipede sand-wurm ash-golem phoenix-hatchling ember-drake"
)

out="$1"
: > "$out"
for z in portmere starting-road thornwood drowned-coast ashen-reach; do
  d="${zones[$z]}"
  for m in ${pools[$z]}; do
    for danger in 0 "$d"; do
      res=$(dotnet run --no-build -- encounter-sim --monster "$m" --rolls 500 --seed 42 \
                 --player-level 5 --player-element Aether --danger-level "$danger" 2>&1 | tail -20)
      wr=$(echo "$res" | grep -E "Wins" | head -1)
      diff=$(echo "$res" | grep -E "Difficulty" | head -1)
      echo "$z d=$danger mon=$m | $wr | $diff" >> "$out"
    done
  done
done
echo "DONE"
