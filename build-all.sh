#!/usr/bin/env bash
# Build all Rapid SCADA / Arch SCADA solutions in the documented order.
# See CLAUDE.md. Run from the repository root.
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
dotnet build ScadaWeb/ScadaWeb/ScadaWeb.sln -c "$CONFIG" -p:AllowMissingPrunePackageData=true

step "ScadaAdmin"
dotnet build ScadaAdmin/ScadaAdmin/ScadaAdmin.sln -c "$CONFIG"

step "ScadaReport"
dotnet build ScadaReport/ScadaReport.sln -c "$CONFIG"

step "ScadaReport.Tests"
dotnet test tests/ScadaReport.Tests/ScadaReport.Tests.csproj -c "$CONFIG"

step "OpenDrivers"
dotnet build ScadaComm/OpenDrivers/OpenDrivers.sln -c "$CONFIG"

step "OpenDrivers2"
dotnet build ScadaComm/OpenDrivers2/OpenDrivers2.sln -c "$CONFIG"

step "OpenModules"
dotnet build ScadaServer/OpenModules/OpenModules.sln -c "$CONFIG"

step "Mimics"
dotnet build ScadaWeb/Mimics/Mimics.sln -c "$CONFIG" -p:AllowMissingPrunePackageData=true

step "OpenPlugins"
dotnet build ScadaWeb/OpenPlugins/OpenPlugins.sln -c "$CONFIG" -p:AllowMissingPrunePackageData=true

step "OpenExtensions"
dotnet build ScadaAdmin/OpenExtensions/OpenExtensions.sln -c "$CONFIG"

step "ScadaDoc"
dotnet build ScadaDoc/ScadaDoc.sln -c "$CONFIG" -p:AllowMissingPrunePackageData=true

# Arch SCADA libraries & desktop HMI (Avalonia, .NET 10)
if [ -f ArchScada/ArchScada.ControlBoundary/ArchScada.ControlBoundary.csproj ]; then
  step "ArchScada.ControlBoundary"
  dotnet build ArchScada/ArchScada.ControlBoundary/ArchScada.ControlBoundary.csproj -c "$CONFIG"
fi

if [ -f ArchScada/ArchScada.CoreIntegration/ArchScada.CoreIntegration.csproj ]; then
  step "ArchScada.CoreIntegration"
  dotnet build ArchScada/ArchScada.CoreIntegration/ArchScada.CoreIntegration.csproj -c "$CONFIG"
fi

if [ -f ArchScada/ArchScada.Desktop/ArchScada.Desktop.csproj ]; then
  step "ArchScada.Desktop"
  dotnet build ArchScada/ArchScada.Desktop/ArchScada.Desktop.csproj -c "$CONFIG"
fi

if [ -f ArchScada/ArchScada.PlcSimulator/ArchScada.PlcSimulator.csproj ]; then
  step "ArchScada.PlcSimulator"
  dotnet build ArchScada/ArchScada.PlcSimulator/ArchScada.PlcSimulator.csproj -c "$CONFIG"
fi

echo
echo "All builds completed successfully."
