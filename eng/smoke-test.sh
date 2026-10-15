#!/usr/bin/env bash
# Install the packed packages (artifacts/package/release) into tests/SmokeTest the way an app would,
# and check that photocraft-cli is found and runs in every output layout:
#
#   1. dotnet build (framework-dependent)   -> runtimes/<rid>/native/photocraft-cli
#   2. dotnet publish -r <rid>              -> photocraft-cli next to the app
#   3. dotnet publish -r <rid> single-file  -> photocraft-cli next to the app, not bundled
#   4. PHOTOCRAFT_CLI_PATH override (good and bad path)
#   5. .NET Framework 4.8 build             -> runtimes/win-*/native/photocraft-cli.exe (run on Windows only)
#
# Usage: eng/smoke-test.sh <rid> [package revision]
#   rid                 this machine: linux-x64, linux-arm64, osx-arm64, osx-x64, win-x64, win-x86, win-arm64
#   package revision    1-99 (default 1): PhotoCraft 0.8.2 revision 1 is package version 0.8.201
# The PhotoCraft version is $PHOTOCRAFT_VERSION, else the one eng/download-cli.cs downloaded (runtimes/.version).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RID="${1:?usage: eng/smoke-test.sh <rid> [package revision]}"
REVISION="${2:-1}"
VERSION="${PHOTOCRAFT_VERSION:-$(cat "$ROOT/runtimes/.version")}"
IFS=. read -r MAJOR MINOR PATCH <<<"$VERSION"
PACKAGE_VERSION="$MAJOR.$MINOR.$((PATCH * 100 + REVISION))"
PROJECT="$ROOT/tests/SmokeTest/SmokeTest.csproj"
OUT="$ROOT/artifacts/smoke"

case "$RID" in
  win-*) EXE=photocraft-cli.exe; APP=SmokeTest.exe ;;
  *) EXE=photocraft-cli; APP=SmokeTest ;;
esac
case "$RID" in
  osx-*) PACKAGE_RID=osx ;; # one universal binary
  *) PACKAGE_RID="$RID" ;;
esac

step() { echo; echo "==> $*"; }
fail() { echo "FAIL: $*" >&2; exit 1; }
expect_file() {
  [ -f "$1" ] || fail "$1 is missing"
  case "$RID" in win-*) ;; *) [ -x "$1" ] || fail "$1 is not executable (the buildTransitive chmod didn't run)" ;; esac
}

# Always test the freshly packed packages, never an older copy from the cache.
rm -rf "$OUT"
export PhotoCraftPackageVersion="$PACKAGE_VERSION"

step "1. framework-dependent build: runtimes/$PACKAGE_RID/native/$EXE"
dotnet build "$PROJECT" -c Release -f net10.0 -o "$OUT/build"
expect_file "$OUT/build/runtimes/$PACKAGE_RID/native/$EXE"
dotnet "$OUT/build/SmokeTest.dll" "$VERSION"

step "2. publish -r $RID: $EXE next to the app"
dotnet publish "$PROJECT" -c Release -f net10.0 -r "$RID" --self-contained false -o "$OUT/publish-rid"
expect_file "$OUT/publish-rid/$EXE"
[ ! -d "$OUT/publish-rid/runtimes" ] || fail "publish -r $RID still has a runtimes/ folder"
"$OUT/publish-rid/$APP" "$VERSION"

step "3. single-file self-contained publish: $EXE next to the app, not bundled"
dotnet publish "$PROJECT" -c Release -f net10.0 -r "$RID" --self-contained -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o "$OUT/single-file"
expect_file "$OUT/single-file/$EXE"
"$OUT/single-file/$APP" "$VERSION"

step "4. $EXE from PHOTOCRAFT_CLI_PATH"
mkdir -p "$OUT/override"
cp "$OUT/publish-rid/$EXE" "$OUT/override/$EXE"
PHOTOCRAFT_CLI_PATH="$OUT/override/$EXE" dotnet "$OUT/build/SmokeTest.dll" "$VERSION" | tee "$OUT/override.log"
grep -q "override" "$OUT/override.log" || fail "PHOTOCRAFT_CLI_PATH was ignored"
if PHOTOCRAFT_CLI_PATH="$OUT/override/missing" dotnet "$OUT/build/SmokeTest.dll" "$VERSION" 2>/dev/null; then
  fail "a PHOTOCRAFT_CLI_PATH that doesn't exist was accepted"
fi

step "5. .NET Framework 4.8 build: runtimes/win-*/native/photocraft-cli.exe"
dotnet build "$PROJECT" -c Release -f net48 -o "$OUT/netfx"
for rid in win-x64 win-x86 win-arm64; do
  [ -f "$OUT/netfx/runtimes/$rid/native/photocraft-cli.exe" ] || fail "net48 output lacks runtimes/$rid/native/photocraft-cli.exe"
done
[ ! -e "$OUT/netfx/runtimes/linux-x64" ] || fail "net48 output has the Linux binary; the meta-package should only bring Windows ones"
case "$RID" in win-*) "$OUT/netfx/SmokeTest.exe" "$VERSION" ;; *) echo "(not run: .NET Framework needs Windows)" ;; esac

step "all smoke tests passed on $RID (PhotoCraft $VERSION, packages $PACKAGE_VERSION)"
