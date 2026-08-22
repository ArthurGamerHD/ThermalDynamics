#!/usr/bin/env bash
# Assembles the interactive corpus report from its three parts plus a data payload.
#   ./build-report.sh <data-dir> [out.html]
# The data payload is produced by pack.py from outcomes.csv in the same directory.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
data="${1:?usage: build-report.sh <data-dir> [out.html]}"
out="${2:-$data/report.html}"

python3 "$here/pack.py" "$data"
cat "$here/shell.html" "$here/body.html" > "$out"
printf '<script id="corpus-data" type="application/json">' >> "$out"
cat "$data/corpus.json" >> "$out"
printf '</script>\n' >> "$out"
cat "$here/script.html" >> "$out"
echo "wrote $out ($(du -h "$out" | cut -f1))"
