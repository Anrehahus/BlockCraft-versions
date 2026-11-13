#!/bin/sh
cd "$(dirname "$0")"
[ -d node_modules ] || npm install
nohup ./node_modules/.bin/electron . >/dev/null 2>&1 &
