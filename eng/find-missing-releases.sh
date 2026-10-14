#!/usr/bin/env bash
# Print, as a JSON array, the upstream PhotoCraft releases to publish now, e.g. ["0.8.2","0.9.0"]:
# stable releases (not drafts, not pre-releases) of at least <min version>, published upstream at least
# <min age hours> ago, with no ArtCraft.PhotoCraft.Runtime package on nuget.org yet. Progress goes to stderr.
#
# Usage: eng/find-missing-releases.sh <min version> <min age hours>
# Needs: gh (with GH_TOKEN), curl, jq, GNU date.
set -euo pipefail

MIN_VERSION="${1:?usage: eng/find-missing-releases.sh <min version> <min age hours>}"
MIN_AGE_HOURS="${2:?usage: eng/find-missing-releases.sh <min version> <min age hours>}"
UPSTREAM=storytold/photocraft
MARKER_PACKAGE=artcraft.photocraft.runtime # the meta-package, pushed last

log() { echo "$*" >&2; }
version_ge() { [ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | head -n 1)" = "$2" ]; }

# Package versions carry a two-digit revision in the patch number: 0.8.201 and 0.8.202 are upstream 0.8.2.
index="$(mktemp)"
trap 'rm -f "$index"' EXIT
status="$(curl -sS --retry 3 -o "$index" -w '%{http_code}' "https://api.nuget.org/v3-flatcontainer/$MARKER_PACKAGE/index.json")"
case "$status" in
  200) published="$(jq -r '.versions[]' "$index" | awk -F. '{ printf "%s.%s.%d\n", $1, $2, int($3 / 100) }' | sort -u)" ;;
  404) published="" ;; # nothing published yet
  *) log "error: nuget.org answered HTTP $status"; exit 1 ;;
esac

cutoff=$(( $(date -u +%s) - MIN_AGE_HOURS * 3600 ))
releases="$(gh release list --repo "$UPSTREAM" --limit 200 --exclude-drafts --exclude-pre-releases \
  --json tagName,publishedAt,isDraft,isPrerelease \
  --jq '.[] | select((.isDraft or .isPrerelease) | not) | "\(.tagName) \(.publishedAt)"')"

missing=()
while read -r tag published_at; do
  [ -n "$tag" ] || continue
  version="${tag#v}"
  if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then log "$tag: skipped, not a plain X.Y.Z version"; continue; fi
  if ! version_ge "$version" "$MIN_VERSION"; then continue; fi
  if grep -qxF "$version" <<<"$published"; then log "$version: already on nuget.org"; continue; fi
  if [ "$(date -u -d "$published_at" +%s)" -gt "$cutoff" ]; then
    log "$version: released $published_at, waiting until it is ${MIN_AGE_HOURS}h old"
    continue
  fi
  log "$version: to publish"
  missing+=("$version")
done <<<"$releases"

printf '%s\n' ${missing[@]+"${missing[@]}"} | jq -R 'select(length > 0)' | jq -sc .
