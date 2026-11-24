#!/usr/bin/env bash
# Install Arch SCADA Desktop HMI and Rapid SCADA backend services onto the local Linux workstation.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

PREFIX="$HOME/.local/share/archscada"
BIN_DIR="$HOME/.local/bin"

usage() {
  cat <<EOF
Usage: $(basename "$0") [--prefix <dir>] [--bin-dir <dir>]

Options:
  --prefix <dir>   Installation prefix (default: $HOME/.local/share/archscada)
  --bin-dir <dir>  Wrapper script directory (default: $HOME/.local/bin)
  -h, --help       Show this help message
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix)
      [[ $# -ge 2 ]] || { echo "Missing value for --prefix" >&2; exit 1; }
      PREFIX="$2"
      shift 2
      ;;
    --bin-dir)
      [[ $# -ge 2 ]] || { echo "Missing value for --bin-dir" >&2; exit 1; }
      BIN_DIR="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

# Resolve absolute paths without requiring target directories to exist yet
mkdir -p "$PREFIX" "$BIN_DIR"
PREFIX="$(cd "$PREFIX" && pwd)"
BIN_DIR="$(cd "$BIN_DIR" && pwd)"

echo "Installing Arch SCADA suite:"
echo "  Repo root : $REPO_ROOT"
echo "  Prefix    : $PREFIX"
echo "  Bin dir   : $BIN_DIR"

publish_project() {
  local name="$1"
  local csproj="$2"
  local out_dir="$3"
  shift 3

  echo
  echo "===== Publishing $name -> $out_dir ====="
  rm -rf "$out_dir"
  mkdir -p "$out_dir"
  dotnet publish "$REPO_ROOT/$csproj" -c Release -o "$out_dir" "$@"
}

publish_project "ArchScada.Desktop" "ArchScada/ArchScada.Desktop/ArchScada.Desktop.csproj" "$PREFIX/ArchScada.Desktop"
publish_project "ScadaServer"       "ScadaServer/ScadaServer/ScadaServerApp/ScadaServerApp.csproj" "$PREFIX/ScadaServer"
publish_project "ScadaComm"         "ScadaComm/ScadaComm/ScadaCommApp/ScadaCommApp.csproj" "$PREFIX/ScadaComm"
publish_project "ScadaAgent"        "ScadaAgent/ScadaAgent/ScadaAgentApp/ScadaAgentApp.csproj" "$PREFIX/ScadaAgent"
publish_project "ScadaWeb"          "ScadaWeb/ScadaWeb/ScadaWeb/ScadaWeb.csproj" "$PREFIX/ScadaWeb" -p:AllowMissingPrunePackageData=true

echo
echo "===== Copying ArchCoalPlant configuration snapshot ====="
rm -rf "$PREFIX/Projects/ArchCoalPlant"
mkdir -p "$PREFIX/Projects"
cp -a "$REPO_ROOT/Projects/ArchCoalPlant" "$PREFIX/Projects/ArchCoalPlant"

echo
echo "===== Installing executable wrappers in $BIN_DIR ====="

cat > "$BIN_DIR/archscada" <<EOF
#!/usr/bin/env bash
exec "$PREFIX/ArchScada.Desktop/ArchScada.Desktop" "\$@"
EOF
chmod +x "$BIN_DIR/archscada"

cat > "$BIN_DIR/archscada-server" <<EOF
#!/usr/bin/env bash
exec dotnet "$PREFIX/ScadaServer/ScadaServerApp.dll" "\$@"
EOF
chmod +x "$BIN_DIR/archscada-server"

cat > "$BIN_DIR/archscada-comm" <<EOF
#!/usr/bin/env bash
exec dotnet "$PREFIX/ScadaComm/ScadaCommApp.dll" "\$@"
EOF
chmod +x "$BIN_DIR/archscada-comm"

cat > "$BIN_DIR/archscada-web" <<EOF
#!/usr/bin/env bash
exec dotnet "$PREFIX/ScadaWeb/ScadaWeb.dll" "\$@"
EOF
chmod +x "$BIN_DIR/archscada-web"

echo
echo "===== Installing application icon and desktop entry ====="
ICON_DST="$PREFIX/archscada.ico"
cp -f "$REPO_ROOT/ArchScada/ArchScada.Desktop/Assets/avalonia-logo.ico" "$ICON_DST"

APPS_DIR="$HOME/.local/share/applications"
mkdir -p "$APPS_DIR"
cat > "$APPS_DIR/archscada.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Arch SCADA 3D HMI
Comment=Coal Handling & Preparation Plant (CHPP) 3D Digital Twin & Operator HMI
Exec=$BIN_DIR/archscada
Icon=$ICON_DST
Terminal=false
Categories=Engineering;Science;Utility;
StartupWMClass=ArchScada.Desktop
EOF

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
fi

echo
echo "Installation complete!"
echo "  Desktop HMI : $BIN_DIR/archscada"
echo "  SCADA Server: $BIN_DIR/archscada-server"
echo "  SCADA Comm  : $BIN_DIR/archscada-comm"
echo "  SCADA Web   : $BIN_DIR/archscada-web"
echo "  Desktop File: $APPS_DIR/archscada.desktop"
