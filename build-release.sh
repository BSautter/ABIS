#!/usr/bin/env bash
#
# build-release.sh — produce the ABIS native-install release tarball.
#
# Publishes a self-contained linux-x64 build of the API (+ web UIs) and bundles
# it into dist/abis-<version>-linux-x64.tar.gz. The server needs nothing
# pre-installed: the .NET runtime is bundled and the Oracle driver is managed.
#
# Phase 1 (docs/INSTALL_PLAN.md): the tarball carries the published app under
# app/. The deploy/ scripts (install.sh, abis.service, nginx config) are copied
# in automatically once they exist (Phases 2-3) — this script already looks for
# them, so it keeps working unchanged as those land.
#
# Usage:
#   bash build-release.sh [--version X.Y.Z] [--app-out DIR]
#
# --app-out keeps a copy of the published app where the caller asks, instead of
# letting it die with the staging directory. The release workflow hands that copy to
# build-deb.sh --from, so the tarball and the .deb are the SAME build rather than two
# separate compilations of the same source that merely ought to agree.
#
# Version resolution (when --version is omitted):
#   git describe --tags --always --dirty  ->  e.g. v1.2.3, v1.2.3-4-g1a2b3c, 1a2b3c
#   falls back to 0.0.0-dev outside a git checkout.
#
set -euo pipefail

# --- locate the repo root (this script lives at the root) --------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

API_PROJECT="api/src/ABIS.Api"
PUBLISH_PROFILE="linux-x64"
RID="linux-x64"

# --- parse args --------------------------------------------------------------
RAW_VERSION=""
APP_OUT=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      RAW_VERSION="${2:-}"; shift 2 ;;
    --version=*)
      RAW_VERSION="${1#*=}"; shift ;;
    --app-out)
      APP_OUT="${2:-}"; shift 2 ;;
    --app-out=*)
      APP_OUT="${1#*=}"; shift ;;
    -h|--help)
      grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)
      echo "error: unknown argument: $1" >&2; exit 2 ;;
  esac
done

# --- resolve version ---------------------------------------------------------
if [[ -z "$RAW_VERSION" ]]; then
  if git rev-parse --git-dir >/dev/null 2>&1; then
    RAW_VERSION="$(git describe --tags --always --dirty 2>/dev/null || echo "0.0.0-dev")"
  else
    RAW_VERSION="0.0.0-dev"
  fi
fi

# Strip a leading "v" and pull out the leading X.Y.Z for the numeric assembly
# version (.NET's /p:Version is strict); the full string rides in
# InformationalVersion.
CLEAN_VERSION="${RAW_VERSION#v}"
if [[ "$CLEAN_VERSION" =~ ^([0-9]+\.[0-9]+\.[0-9]+) ]]; then
  NUMERIC_VERSION="${BASH_REMATCH[1]}"
else
  NUMERIC_VERSION="0.0.0"
fi

PKG_NAME="abis-${CLEAN_VERSION}-${RID}"
STAGE_DIR="$(mktemp -d)"
PKG_DIR="${STAGE_DIR}/${PKG_NAME}"
DIST_DIR="${SCRIPT_DIR}/dist"
TARBALL="${DIST_DIR}/${PKG_NAME}.tar.gz"

cleanup() { rm -rf "$STAGE_DIR"; }
trap cleanup EXIT

echo "==> ABIS release build"
echo "    version (full)    : ${RAW_VERSION}"
echo "    version (assembly): ${NUMERIC_VERSION}"
echo "    runtime           : ${RID}"
echo "    package           : ${PKG_NAME}.tar.gz"
echo

# --- publish -----------------------------------------------------------------
echo "==> dotnet publish (self-contained, ${RID})"
mkdir -p "${PKG_DIR}/app"
# Use -p: (not /p:) so this is immune to git-bash/MSYS path mangling on Windows;
# both forms are equivalent on Linux CI.
dotnet publish "$API_PROJECT" \
  -c Release \
  "-p:PublishProfile=${PUBLISH_PROFILE}" \
  "-p:Version=${NUMERIC_VERSION}" \
  "-p:InformationalVersion=${RAW_VERSION}" \
  -o "${PKG_DIR}/app"

# Ensure the Linux apphost ships executable. A cross-publish from Windows leaves
# it at 0644; on Linux CI dotnet already marks it +x (this is then a no-op).
chmod +x "${PKG_DIR}/app/ABIS.Api"

# Hand the published tree to the caller (the release workflow passes it to
# build-deb.sh --from). A ~50 MB copy costs a second; publishing a second time costs
# minutes, and yields a DIFFERENT binary from the one already in the tarball.
if [[ -n "$APP_OUT" ]]; then
  echo "==> copying the published app to ${APP_OUT}"
  rm -rf "$APP_OUT"
  mkdir -p "$APP_OUT"
  cp -R "${PKG_DIR}/app/." "${APP_OUT}/"
fi

# --- bundle deploy assets (present from Phase 2 onward) ----------------------
if [[ -d "${SCRIPT_DIR}/deploy" ]]; then
  echo "==> bundling deploy/ assets"
  cp -R "${SCRIPT_DIR}/deploy/." "${PKG_DIR}/"
  # convenience: top-level install.sh stays executable in the tarball
  [[ -f "${PKG_DIR}/install.sh" ]] && chmod +x "${PKG_DIR}/install.sh"
  [[ -f "${PKG_DIR}/uninstall.sh" ]] && chmod +x "${PKG_DIR}/uninstall.sh"
else
  echo "==> deploy/ not present yet (Phase 1) — packaging app/ only"
fi

# stamp the version into the package for traceability
echo "${RAW_VERSION}" > "${PKG_DIR}/VERSION"

# --- tar ---------------------------------------------------------------------
echo "==> writing ${TARBALL}"
mkdir -p "$DIST_DIR"
rm -f "$TARBALL"
tar -C "$STAGE_DIR" -czf "$TARBALL" "$PKG_NAME"

SIZE="$(du -h "$TARBALL" | cut -f1)"
echo
echo "==> done: ${TARBALL} (${SIZE})"
echo
echo "Manual smoke test (Phase 1):"
echo "  tar -xzf ${PKG_NAME}.tar.gz && cd ${PKG_NAME}/app"
echo "  ASPNETCORE_ENVIRONMENT=Production \\"
echo "  ASPNETCORE_URLS=http://127.0.0.1:8080 \\"
echo "  Database__Provider=Oracle \\"
echo "  Database__ConnectionString='<oracle-conn>' \\"
echo "  ApiKeys__Keys__0='<key>' \\"
echo "  ./ABIS.Api"
echo "  # then: curl http://127.0.0.1:8080/health/ready"
