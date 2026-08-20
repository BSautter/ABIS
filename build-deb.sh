#!/usr/bin/env bash
#
# build-deb.sh — build the ABIS Debian package (native systemd service).
#
# Produces dist/abis_<version>_amd64.deb: a self-contained ASP.NET Core app
# (.NET bundled) installed as a hardened systemd service, with an `abis-configure`
# helper for the Oracle connection / API key / optional nginx + TLS. The .deb does
# the file + service lifecycle; `abis-configure` reuses install.sh --configure-only
# for the runtime config (no duplicated logic).
#
# Must run on a Debian/Ubuntu host (needs dpkg-deb) with the .NET 8 SDK.
#
#   bash build-deb.sh [--version X.Y.Z] [--from <published-app-dir>]
#
# --from packages an app directory that has ALREADY been published (by
# build-release.sh) instead of publishing again. The release workflow uses it so the
# tarball and the .deb are the SAME build, rather than two separate compilations of
# the same source that merely ought to agree. Without it the script publishes for
# itself, so an ad-hoc local `bash build-deb.sh` still works on its own.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

API_PROJECT="api/src/ABIS.Api"
PUBLISH_PROFILE="linux-x64"
ARCH="amd64"

# --- args / version ----------------------------------------------------------
RAW_VERSION=""
FROM_DIR=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)   RAW_VERSION="${2:-}"; shift 2 ;;
    --version=*) RAW_VERSION="${1#*=}"; shift ;;
    --from)      FROM_DIR="${2:-}"; shift 2 ;;
    --from=*)    FROM_DIR="${1#*=}"; shift ;;
    -h|--help)   grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)           echo "error: unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$RAW_VERSION" ]]; then
  if git rev-parse --git-dir >/dev/null 2>&1; then
    RAW_VERSION="$(git describe --tags --always --dirty 2>/dev/null || echo "0.0.0-dev")"
  else
    RAW_VERSION="0.0.0-dev"
  fi
fi

# Debian upstream version: strip a leading 'v'. The numeric X.Y.Z drives the
# assembly version; the full string rides in InformationalVersion.
DEB_VERSION="${RAW_VERSION#v}"
if [[ "$DEB_VERSION" =~ ^([0-9]+\.[0-9]+\.[0-9]+) ]]; then
  NUMERIC_VERSION="${BASH_REMATCH[1]}"
else
  NUMERIC_VERSION="0.0.0"
fi
# Debian requires the upstream version to start with a digit. An untagged
# `git describe` can return a bare commit SHA (e.g. 'a1b2c3d-dirty') whose first
# char is a hex letter, which dpkg-deb rejects ("version number does not start
# with digit"). Coerce to a policy-valid, digit-leading value, mapping any other
# disallowed char to '+'. CI always passes --version vX.Y.Z, so this only affects
# ad-hoc local builds.
if ! [[ "$DEB_VERSION" =~ ^[0-9] ]]; then
  DEB_VERSION="0.0.0+$(printf '%s' "$DEB_VERSION" | tr -c 'A-Za-z0-9.~+' '+')"
fi

command -v dpkg-deb >/dev/null 2>&1 || { echo "error: dpkg-deb not found — build on a Debian/Ubuntu host or in CI." >&2; exit 1; }
# The SDK is only needed when this script publishes for itself.
if [[ -z "$FROM_DIR" ]]; then
  command -v dotnet >/dev/null 2>&1 || { echo "error: dotnet SDK not found." >&2; exit 1; }
elif [[ ! -f "${FROM_DIR}/ABIS.Api" ]]; then
  # Fail here rather than shipping a .deb with an empty /opt/abis/app, which installs
  # cleanly and then fails to start on the plant's server.
  #
  # -f, not -x. The apphost's execute bit is exactly the thing that does NOT survive a
  # cross-publish from Windows — which is why the chmod below exists at all — so testing
  # for it would refuse a perfectly good tree whenever the pair is run from an MSYS
  # shell. (Verified: it does. The question this guard asks is "is there a published app
  # here", and -f is that question.)
  echo "error: --from '${FROM_DIR}' does not contain ABIS.Api — is that a published app directory?" >&2
  exit 1
fi

PKG_NAME="abis_${DEB_VERSION}_${ARCH}"
STAGE_DIR="$(mktemp -d)"
PKGROOT="${STAGE_DIR}/pkg"
DIST_DIR="${SCRIPT_DIR}/dist"
DEB_OUT="${DIST_DIR}/${PKG_NAME}.deb"
trap 'rm -rf "$STAGE_DIR"' EXIT

echo "==> ABIS .deb build"
echo "    version  : ${RAW_VERSION} (deb ${DEB_VERSION}, assembly ${NUMERIC_VERSION})"
echo "    package  : ${PKG_NAME}.deb"
echo

# --- filesystem tree ---------------------------------------------------------
mkdir -p "${PKGROOT}/opt/abis/app" \
         "${PKGROOT}/lib/systemd/system" \
         "${PKGROOT}/usr/share/abis/nginx" \
         "${PKGROOT}/usr/sbin" \
         "${PKGROOT}/usr/share/doc/abis" \
         "${PKGROOT}/DEBIAN"

if [[ -n "$FROM_DIR" ]]; then
  echo "==> reusing the app already published to ${FROM_DIR}"
  cp -R "${FROM_DIR}/." "${PKGROOT}/opt/abis/app/"
else
  echo "==> dotnet publish (self-contained ${PUBLISH_PROFILE})"
  dotnet publish "$API_PROJECT" -c Release \
    "-p:PublishProfile=${PUBLISH_PROFILE}" \
    "-p:Version=${NUMERIC_VERSION}" \
    "-p:InformationalVersion=${RAW_VERSION}" \
    -o "${PKGROOT}/opt/abis/app"
fi
chmod +x "${PKGROOT}/opt/abis/app/ABIS.Api"

echo "==> staging package files"
install -m 0644 deploy/abis.service            "${PKGROOT}/lib/systemd/system/abis.service"
install -m 0644 deploy/nginx/abis.conf         "${PKGROOT}/usr/share/abis/nginx/abis.conf"
install -m 0644 deploy/nginx/abis-tls.conf     "${PKGROOT}/usr/share/abis/nginx/abis-tls.conf"
install -m 0644 deploy/nginx/abis-pending.conf "${PKGROOT}/usr/share/abis/nginx/abis-pending.conf"
install -m 0755 deploy/install.sh              "${PKGROOT}/usr/share/abis/install.sh"
install -m 0755 deploy/abis-configure          "${PKGROOT}/usr/sbin/abis-configure"
install -m 0644 deploy/abis.answers.example    "${PKGROOT}/usr/share/doc/abis/abis.answers.example"
install -m 0644 deploy/debian/copyright        "${PKGROOT}/usr/share/doc/abis/copyright"

# Debian changelog (gzipped, mode 0644).
{
  echo "abis (${DEB_VERSION}) stable; urgency=medium"
  echo
  echo "  * Release ${RAW_VERSION}."
  echo
  echo " -- mattiIce <noreply@users.noreply.github.com>  $(date -u +'%a, %d %b %Y %H:%M:%S +0000')"
} > "${PKGROOT}/usr/share/doc/abis/changelog"
gzip -9n "${PKGROOT}/usr/share/doc/abis/changelog"

# --- control + maintainer scripts -------------------------------------------
# Installed size (KiB) for the control file.
INSTALLED_KB="$(du -ks "${PKGROOT}" | cut -f1)"
sed -e "s|@VERSION@|${DEB_VERSION}|g" \
    -e "s|@ARCH@|${ARCH}|g" \
    deploy/debian/control > "${PKGROOT}/DEBIAN/control"
echo "Installed-Size: ${INSTALLED_KB}" >> "${PKGROOT}/DEBIAN/control"

install -m 0755 deploy/debian/postinst "${PKGROOT}/DEBIAN/postinst"
install -m 0755 deploy/debian/prerm    "${PKGROOT}/DEBIAN/prerm"
install -m 0755 deploy/debian/postrm   "${PKGROOT}/DEBIAN/postrm"

# /etc/abis is created at runtime by abis-configure (it holds secrets), so it is
# intentionally NOT shipped and NOT listed as a conffile.

# --- build -------------------------------------------------------------------
mkdir -p "$DIST_DIR"
rm -f "$DEB_OUT"
echo "==> dpkg-deb --build"
dpkg-deb --build --root-owner-group "$PKGROOT" "$DEB_OUT"

echo
echo "==> done: ${DEB_OUT}"
dpkg-deb --info "$DEB_OUT" | sed 's/^/    /'
echo
echo "Install:   sudo apt install ./dist/${PKG_NAME}.deb"
echo "Configure: sudo abis-configure"
