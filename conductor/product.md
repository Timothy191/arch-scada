# Product Definition: Arch SCADA (CHPP 3D Digital Twin & Industrial Automation Platform)

## Vision & Overview
Arch SCADA is an industrial monitoring, process simulation, and 3D Digital Twin platform built on Rapid SCADA v6 and .NET 10, tailored for Coal Handling and Preparation Plants (CHPP). It provides a cross-platform Avalonia Desktop HMI (`ArchScada.Desktop`) with hardware-accelerated OpenGL 3.2 Core / OpenGL ES 3.0 3-pass HDR PBR rendering (`Plant3DOpenGlCanvas`), a read-only ASP.NET Core Web 3D Digital Twin portal (`ScadaWeb` `/DigitalTwin3D`) powered by Three.js, a safety control boundary engine (`ArchScada.ControlBoundary`), and an automated 4-server free 3D Model Context Protocol (MCP) asset and shader pipeline (`blender-mcp`, `3d-asset-processing-mcp`, `openusd-mcp`, `shader-polyhaven-mcp`).

## Core Capabilities
1. **3D CHPP General Arrangement Digital Twin:** 24 glTF 2.0 binary (`.glb`) machinery and sub-equipment assets across Stages 1–6, Wash Plant (`WP-301`), Dense-Medium Cyclone (`DMC-301`), and High-Rate Thickener (`TK-201`).
2. **Safety-First Industrial Control:** ISA-18.2 alarm lifecycle management, read-only web telemetry isolation, and `ControlBoundaryEngine` command validation.
3. **Zero-Cost 3D MCP Engineering Pipeline:** Local `stdio` MCP servers for procedural Blender 5.2 modeling, `gltf-transform` asset optimization, Pixar `usd-core` OpenUSD stage inspection, and Khronos `glslangValidator` shader verification.
