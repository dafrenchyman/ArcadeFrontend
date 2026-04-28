#!/usr/bin/env bash

find . -type f -iname '*.mp4' -print0 | while IFS= read -r -d '' file; do
  out="${file%.*}.ogv"
  [ -f "$out" ] && continue
  ffmpeg -i "$file" -c:v libtheora -q:v 7 -c:a libvorbis -q:a 4 "$out"
done


find . -type f -iname '*.mp4' -exec bash -c '
for file do
  out="${file%.*}.ogv"
  [ -f "$out" ] && continue
  echo "Converting: $file"
  ffmpeg -y -i "$file" -c:v libtheora -q:v 7 -c:a libvorbis -q:a 4 "$out"
done
' bash {} +
