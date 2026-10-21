#!/usr/bin/env bash
# Run the brain on the whole case library (validation/real-storm-cases/brain_cases.json), CHUNK cases per Unity process.
#   bash tools/run_brain_cases.sh [hours=3] [chunk=5] [fresh]
# Resumes by default: cases already in brain_cases_runs.json are skipped. "fresh" starts over.
# One process per chunk because batch-mode Unity never ends a frame, so D3D12 never frees released buffers and readback
# staging: one process ran out of video memory after ~7 three-hour runs. The suite accumulates results in
# validation/real-storm-cases/brain_cases_runs.json and rewrites validation/reports/ValidationReport_cases.md after
# every chunk. Logs: validation_cases_<n>.log in the repo root (gitignored).
set -u
cd "$(dirname "$0")/.."
HOURS=${1:-3}; CHUNK=${2:-5}; FRESH=${3:-}
UNITY="/c/Program Files/Unity/Hub/Editor/6000.6.6f1/Editor/Unity.exe"
python -I tools/brain_case_manifest.py
# tr: Python prints CRLF here, and a trailing \r on each id breaks the -dynCases match
mapfile -t IDS < <(python -I - "$FRESH" <<'PY' | tr -d '\r'
import json, sys
from pathlib import Path
done = set()
runs = Path("validation/real-storm-cases/brain_cases_runs.json")
if sys.argv[1] != "fresh" and runs.exists():
    done = {r["c"]["id"] for r in json.loads(runs.read_text())["runs"]}
for c in json.load(open("validation/real-storm-cases/brain_cases.json"))["cases"]:
    if c["id"] not in done:
        print(c["id"])
PY
)
echo "${#IDS[@]} cases to run"
n=0; fresh=""; [ "$FRESH" = "fresh" ] && fresh="-dynCasesFresh"
for ((i = 0; i < ${#IDS[@]}; i += CHUNK)); do
  chunk=$(IFS=,; echo "${IDS[*]:i:CHUNK}")
  n=$((n + 1))
  echo "chunk $n: $chunk"
  "$UNITY" -batchmode -projectPath tests/SupercellLabTest -executeMethod StormLab.Supercell.EditorTools.DynamicsValidation.RunFromCommandLine \
    -dynSuite cases -dynCases "$chunk" -dynCaseHours "$HOURS" $fresh -logFile "validation_cases_$n.log"
  echo "  exit $?: $(grep -c '\[Dynamics\] case' "validation_cases_$n.log") cases logged"
  fresh=""
done
echo done
