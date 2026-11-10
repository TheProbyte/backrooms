#!/usr/bin/env bash
# Captures every frame in 60-frame chunks, each with a fresh Edge (one long Edge session slows to a crawl after
# ~100 frames), two chunks at a time. Only chunks with missing frames are (re)run, so it resumes and never repeats.
# bash render.sh <framesDir (Windows path)> [captions 0|1]
set -u
OUT="$1"; CAP="${2:-0}"
TOTAL=3960; CH=60
here="$(cd "$(dirname "$0")" && pwd)"
dir="$(cygpath -u "$OUT" 2>/dev/null || echo "$OUT")"
mkdir -p "$dir"
missing() { # chunk start -> 0 if every frame in it exists (and is not empty)
  for ((n=$1; n<$1+CH && n<TOTAL; n++)); do f=$(printf "%s/f%05d.png" "$dir" $n); [ -s "$f" ] || return 0; done; return 1; }
for pass in 1 2 3 4; do
  todo=(); for ((a=0; a<TOTAL; a+=CH)); do missing $a && todo+=($a); done
  [ ${#todo[@]} -eq 0 ] && break
  echo "pass $pass: ${#todo[@]} chunks to do"
  for ((i=0; i<${#todo[@]}; i+=2)); do
    for a in "${todo[@]:i:2}"; do
      find "$dir" -name 'f*.png' -size 0 -delete 2>/dev/null
      timeout 50 node "$here/capture.mjs" "$OUT" $a $((a+CH-1)) 1 $CAP > /dev/null 2>&1 &
    done
    wait
  done
done
powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='msedge.exe'\" | Where-Object { \$_.CommandLine -like '*edge-profile-*' } | ForEach-Object { try { Stop-Process -Id \$_.ProcessId -Force -ErrorAction Stop } catch {} }" > /dev/null 2>&1
echo "frames $(find "$dir" -name 'f*.png' -size +0 | wc -l) / $TOTAL"
