#!/usr/bin/env bash
# Build all Rapid SCADA / Arch SCADA solutions in the documented order.
# See HowToBuild.txt. Run from the repository root.
set -euo pipefail

CONFIG="${1:-Release}"

step() { echo; echo "===== $* ====="; }

step "ScadaCommon"
dotnet build ScadaCommon/ScadaCommon.sln -c "$CONFIG"

step "ScadaAgent"
dotnet build ScadaAgent/ScadaAgent/ScadaAgent.sln -c "$CONFIG"

step "ScadaComm"
dotnet build ScadaComm/ScadaComm/ScadaComm.sln -c "$CONFIG"

step "ScadaServer"
dotnet build ScadaServer/ScadaServer/ScadaServer.sln -c "$CONFIG"

step "ScadaWeb"
dotnet build ScadaWeb/ScadaWeb/ScadaWeb.sln -c "$CONFIG"

step "ScadaAdmin"
dotnet build ScadaAdmin/ScadaAdmin/ScadaAdmin.sln -c "$CONFIG"

step "ScadaReport"
dotnet build ScadaReport/ScadaReport.sln -c "$CONFIG"

step "OpenDrivers"
dotnet build ScadaComm/OpenDrivers/OpenDrivers.sln -c "$CONFIG"

step "OpenDrivers2"
dotnet build ScadaComm/OpenDrivers2/OpenDrivers2.sln -c "$CONFIG"

step "OpenModules"
dotnet build ScadaServer/OpenModules/OpenModules.sln -c "$CONFIG"

step "Mimics"
dotnet build ScadaWeb/Mimics/Mimics.sln -c "$CONFIG"

step "OpenPlugins"
dotnet build ScadaWeb/OpenPlugins/OpenPlugins.sln -c "$CONFIG"

step "OpenExtensions"
dotnet build ScadaAdmin/OpenExtensions/OpenExtensions.sln -c "$CONFIG"

# Arch SCADA desktop HMI (Avalonia, .NET 10)
if [ -f ArchScada/ArchScada.Desktop/ArchScada.Desktop.csproj ]; then
  step "ArchScada.Desktop"
  dotnet build ArchScada/ArchScada.Desktop/ArchScada.Desktop.csproj -c "$CONFIG"
fi

echo
echo "All builds completed successfully."
