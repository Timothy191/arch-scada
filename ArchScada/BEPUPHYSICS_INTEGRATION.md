# BepuPhysics Digital Twin Integration

## Overview

Adds [BepuPhysics 2.4.0](https://github.com/bepu/bepuphysics2) (Apache-2.0) to the ArchScada Unity HMI for rigid-body
digital-twin simulation of plant equipment. BepuPhysics is a pure .NET 8 rigid-body physics engine, making it a natural
fit for the .NET-based Rapid SCADA codebase.

## Real-World Benefits

| Feature | Benefit |
|---------|---------|
| Operator training simulator | Train operators on a 3D plant twin that behaves like the real process |
| Equipment kinematics | Simulate valves, pumps, conveyors, robot arms with realistic motion |
| Collision detection | Validate AGV/robot cell paths before deploying to hardware |
| Tank level visualization | Realistic fluid sloshing and level changes driven by live SCADA tags |

## Setup

1. Install the NuGet packages:
   ```
   dotnet add package BepuPhysics --version 2.4.0
   dotnet add package BepuUtilities --version 2.4.0
   ```
2. Copy `BepuPhysics.dll` and `BepuUtilities.dll` to `ArchScada/ArchScada.Unity/Assets/Plugins/`.
3. Attach `BepuPhysicsIntegration.cs` to a GameObject in the Unity scene.
4. Use `ScadaTagBridge.cs` to receive live SCADA tag values.
5. Use `DigitalTwinController.cs` to map tag values to 3D transform properties.

## Files

- `Assets/Scripts/DigitalTwin/ScadaTagBridge.cs` — Receives live SCADA tag updates
- `Assets/Scripts/DigitalTwin/DigitalTwinController.cs` — Maps tag values to 3D transforms
- `Assets/Scripts/DigitalTwin/BepuPhysicsIntegration.cs` — BepuPhysics simulation wrapper
- `Packages/manifest.json` — Documents the BepuPhysics dependency