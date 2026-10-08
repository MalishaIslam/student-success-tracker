#!/usr/bin/env bash
# Downloads the Open University Learning Analytics Dataset (OULAD) into ./data
#   bash scripts/get-data.sh
#
# Source: UCI Machine Learning Repository, dataset 349 (CC BY 4.0)
# If the download fails, get the zip in a browser from
#   https://archive.ics.uci.edu/dataset/349/open+university+learning+analytics+dataset
# and unzip it into the data folder; this script only automates that.
set -euo pipefail

URL="${OULAD_URL:-https://archive.ics.uci.edu/static/public/349/open+university+learning+analytics+dataset.zip}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DATA="$ROOT/data"
ZIP="$DATA/oulad.zip"

mkdir -p "$DATA"

if [ -n "$(find "$DATA" -maxdepth 4 -name studentInfo.csv 2>/dev/null | head -n 1)" ]; then
  echo "Dataset already present in $DATA"
  exit 0
fi

echo "Downloading OULAD (about 45 MB)..."
curl -fL --retry 3 -o "$ZIP" "$URL"

echo "Unzipping..."
unzip -oq "$ZIP" -d "$DATA"

# Some copies contain the CSVs inside a second zip file
if [ -z "$(find "$DATA" -maxdepth 4 -name studentInfo.csv | head -n 1)" ]; then
  find "$DATA" -mindepth 1 -maxdepth 3 -name '*.zip' ! -path "$ZIP" -exec unzip -oq {} -d "$DATA" \;
fi

rm -f "$ZIP"

if [ -z "$(find "$DATA" -maxdepth 4 -name studentInfo.csv | head -n 1)" ]; then
  echo "ERROR: studentInfo.csv not found after unzipping. Check the contents of $DATA"
  exit 1
fi

echo "Done. CSV files:"
find "$DATA" -maxdepth 4 -name '*.csv' -exec ls -lh {} \; | awk '{print "  " $5 "\t" $NF}'
