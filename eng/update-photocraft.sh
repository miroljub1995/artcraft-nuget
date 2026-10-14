#!/usr/bin/env bash
# Point eng/PhotoCraft.props at another upstream release: set the version and pin the SHA-256 of each
# archive from that release's SHA256SUMS.txt.
#
# Usage: eng/update-photocraft.sh <version>      e.g. eng/update-photocraft.sh 0.9.0
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROPS="$ROOT/eng/PhotoCraft.props"
NEW="${1:?usage: eng/update-photocraft.sh <version>}"
NEW="${NEW#v}"
OLD="$(sed -n 's:.*<PhotoCraftVersion>\(.*\)</PhotoCraftVersion>.*:\1:p' "$PROPS")"

SUMS="$(curl -fsSL --retry 3 "https://github.com/storytold/photocraft/releases/download/v$NEW/SHA256SUMS.txt")"

sed -i.bak "s:<PhotoCraftVersion>$OLD</PhotoCraftVersion>:<PhotoCraftVersion>$NEW</PhotoCraftVersion>:" "$PROPS"
rm -f "$PROPS.bak"

# For each asset, the archive name with the version filled in, then its digest from SHA256SUMS.txt.
while read -r archive; do
  name="${archive//\$(PhotoCraftVersion)/$NEW}"
  sha="$(printf '%s\n' "$SUMS" | awk -v f="$name" '$2 == f || $2 == "*" f { print $1 }')"
  [ -n "$sha" ] || { echo "error: v$NEW has no $name in SHA256SUMS.txt; the release layout changed" >&2; exit 1; }
  # Replace the Sha256 on the line after this Archive.
  awk -v a="Archive=\"$archive\"" -v s="$sha" '
    hit && /Sha256="/ { sub(/Sha256="[0-9a-f]*"/, "Sha256=\"" s "\""); hit = 0 }
    index($0, a) { hit = 1 }
    { print }
  ' "$PROPS" >"$PROPS.tmp"
  mv "$PROPS.tmp" "$PROPS"
  echo "$name  $sha"
done < <(sed -n 's:.*Archive="\([^"]*\)".*:\1:p' "$PROPS")

echo "eng/PhotoCraft.props: $OLD -> $NEW"
