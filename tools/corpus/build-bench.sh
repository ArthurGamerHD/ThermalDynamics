#!/usr/bin/env bash
# Assembles the balance bench from its three parts plus a packed payload.
#   ./build-bench.sh <survey-dir> <census-dir> [knobs-dir] [out.html]
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
survey="${1:?usage: build-bench.sh <survey-dir> <census-dir> [knobs-dir] [out.html]}"
census="${2:?need a census directory}"
knobs="${3:-}"
out="${4:-$survey/bench.html}"
payload="$(dirname "$out")/bench.json"

python3 "$here/pack-bench.py" "$survey" "$census" "$knobs" "$payload"
cat "$here/bench-shell.html" "$here/bench-body.html" > "$out"
printf '<script id="bench-data" type="application/json">' >> "$out"
cat "$payload" >> "$out"
printf '</script>\n' >> "$out"
cat "$here/bench-script.html" >> "$out"
echo "wrote $out ($(du -h "$out" | cut -f1))"
