#!/usr/bin/env bash
# Builds every platform binary from one machine. Run from the repo root: ./publish.sh
set -euo pipefail

cd "$(dirname "$0")"

echo "Running self-tests first — nothing ships if these fail."
dotnet run --project tests/Brawlhalalla.SelfTest -c Debug --nologo

rm -rf dist
mkdir -p dist

publish() {
  local rid="$1" folder="$2" final="$3"

  echo
  echo "==> $rid"
  dotnet publish src/Brawlhalalla \
    -c Release -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "dist/$folder" \
    --nologo -v quiet

  # One binary per folder, named for the person who has to double-click it.
  if [ -f "dist/$folder/Brawlhalalla.exe" ]; then
    mv "dist/$folder/Brawlhalalla.exe" "dist/$folder/$final"
  else
    mv "dist/$folder/Brawlhalalla" "dist/$folder/$final"
    chmod +x "dist/$folder/$final"
  fi

  # Ship the config alongside so it can be edited without a rebuild. Delete it and the
  # binary falls back to the identical baked-in defaults.
  cp brawlhalalla-config.json "dist/$folder/"
  cp README.md "dist/$folder/" 2>/dev/null || true
}

publish win-x64   windows            "Brawlhalalla-Windows.exe"
publish osx-arm64 mac-apple-silicon  "Brawlhalalla-Mac-AppleSilicon.command"
publish osx-x64   mac-intel          "Brawlhalalla-Mac-Intel.command"
publish linux-x64 linux              "Brawlhalalla-Linux"

echo
echo "Done:"
find dist -maxdepth 2 -type f \( -name 'Brawlhalalla-*' \) -exec ls -lh {} \; \
  | awk '{printf "  %-8s %s\n", $5, $NF}'
