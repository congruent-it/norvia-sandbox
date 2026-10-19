#!/usr/bin/env bash
# Exports the other Norvia icon formats from wwwroot/favicon.svg, which is the source. Needs rsvg-convert (librsvg) and ImageMagick.
# The ICO and the Apple touch icon cannot follow the browser's colour scheme, so they sit on a white tile that reads on any tab or home screen.
set -euo pipefail

cd "$(dirname "$0")/wwwroot"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The mark on a white rounded tile: the tile is the background, the SVG is drawn inside it with a margin.
tile() { # size
  local size=$1 margin=$(($1 / 8)) radius=$(($1 / 5))
  rsvg-convert --width $((size - 2 * margin)) --height $((size - 2 * margin)) favicon.svg -o "$work/mark-$size.png"
  magick -size "${size}x${size}" xc:none -fill white -draw "roundrectangle 0,0 $((size - 1)),$((size - 1)) $radius,$radius" \
    "$work/mark-$size.png" -gravity center -composite "$work/tile-$size.png"
}

for size in 16 32 48; do tile "$size"; done
magick "$work/tile-16.png" "$work/tile-32.png" "$work/tile-48.png" favicon.ico

# iOS fills transparency with black and rounds the corners itself, so the touch icon is a full-bleed white square.
rsvg-convert --width 140 --height 140 favicon.svg -o "$work/touch-mark.png"
magick -size 180x180 xc:white "$work/touch-mark.png" -gravity center -composite apple-touch-icon.png
