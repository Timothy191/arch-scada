# Arch SCADA Visual Style Guide & Coordination Plan

## 1. Brand Identity

| Usage | Color | Hex | Notes |
|---|---|---|---|
| **Amber Accent** | #F59E0B | Used for CTA buttons, active states, highlights. |
| **Teal** | #14B8A6 | Used for status "ok", operational indicators. |
| **Cyan-Blue** | #1D4ED8 | Primary action, links, primary navigation. |
| **Deep Background** | #0D0D0F | Base UI background, dark mode surface. |
| **Text** | #FAFAFA | Primary text on deep background. |

- Minimum contrast ratio: 4.5:1 for normal text, 3:1 for large text.
- Amber accent should not be used as background for large areas; reserve as accent only.
- Teal and cyan-blue are interchangeable for status distinctions, but maintain consistency within a given context.

## 2. Typography

- **Font Family**: `Inter`, weight 400 (regular) as base, 600 (semi-bold) for emphasis, 700 (bold) for titles.
- **Hierarchy**:
  - **Title**: 24px, Inter, weight 700, line-height 32px, tracking tighter (-0.5em).
  - **Label**: 12px, Inter, weight 500, line-height 16px.
  - **Small Text**: 10px, Inter, weight 400, line-height 14px, used for captions, metadata.
- **Responsive Scaling**: Base 16px viewport; scale down 10% for mobile widths < 640px, but never below 10px small text.

## 3. Animation Curves

- **Default Transition**: `cubic-bezier(0.4, 0, 0.2, 1)` (ease-in-out).
- **Procedural Phase**: `smoothstep(t)` for fades, reveals, and LOD transitions.
  - smoothstep defined as `3t² - 2t³` where `t` is normalized time [0,1].
- **Use Cases**:
  - UI element show/hide: 300ms with default curve.
  - Page transition: 500ms with default curve.
  - Equipment sweep animation: 8s total, subdivided per intro sequence (see Section 4).
- **Easing Utilities**:
  ```css
  .ease-default { transition-timing-function: cubic-bezier(0.4, 0, 0.2, 1); }
  .ease-smoothstep { transition-timing-function: cubic-bezier(0.4, 0, 0.2, 1); /* custom shader-driven smoothstep */ }
  ```

## 4. 8‑Second Intro Timing Specification

| Time Range | Segment | Description | Visual Detail |
|---|---|---|---|
| 0 – 2 s | **Silhouette Build** | Dark outlines of plant equipment fade in from black, vertex‑by‑vertex reveal. | Uses smoothstep progression per mesh; total duration 2s. |
| 2 – 4 s | **Scan Sweep** | A horizontal/vertical scan line sweeps across the scene, highlighting equipment in sequence. | Scan line speed: 180°/s; optional particle spark at sweep endpoints. |
| 4 – 6 s | **Title Reveal** | "Arch SCADA" logotype fades/slides in from bottom; amber accent underline animates. | Font: Inter 700 24px; ease-out cubic‑bezier; offset 20px → 0. |
| 6 – 8 s | **Hold + Fade** | Full scene holds; overall opacity fades from 1.0 to 0.0 over the final 1s, then scene hands off to main UI. | Global alpha animation; ensures smooth transition to dashboard. |

- All timing is **absolute** (not dependent on frame rate). Use delta‑time scaled animations in implementation.
- Implement via a coroutine or timeline driver that subdivides each 2‑s segment with 60 fps keyframes.

## 5. Mesh Quality Targets (GLB Assets)

| LOD | Description | Max Vertices | Use Case |
|---|---|---|---|
| **LOD1** | Hero equipment (primary units, turbines, reactors) | 15,000 vertices | Close‑up, interactive, main view. |
| **LOD2** | Standard equipment, secondary units | 8,000 vertices | Mid‑range view, overview panels. |
| **LOD3** | Distant / background props | 2,500 vertices | Far‑away, horizon, batch rendering. |

- **Vertex Budgets by Category**:
  - Vessels/Pressure Tanks: LOD1 12k, LOD2 6k, LOD3 2k.
  - Pumps/Piping: LOD1 8k, LOD2 4k, LOD3 1.5k.
  - Valves/Fittings: LOD1 4k, LOD2 2k, LOD3 800.
  - Structures (buildings, tanks farms): LOD1 20k total (instanced), LOD2 10k, LOD3 3k.
- **Morph Targets**: Limit to 2 per hero asset for animation (e.g., valve opening).
- **Export Format**: GLB 2.0 with Draco compression (quality ~3) enabled, preserving PBR textures.

## 6. PBR Material Library

| Material | Base Color | Roughness | Metallic | Normal | Use Case |
|---|---|---|---|---|---|
| **Steel** | #71717A | 0.35 | 0.85 | Fine micro‑bump | Boilers, piping, structural frames. |
| **Ceramic‑Lined** | #E5E7E8 | 0.45 | 0.20 | Slight wear map | Reaction vessels, high‑temp lines. |
| **Rubber‑Lined** | #F2D9CE | 0.60 | 0.10 | Soft micro‑bump | Gaskets, flexible hoses. |
| **Coal** | #2D2D2D | 0.90 | 0.05 | Sparse dirt map | Fuel storage, conveyors. |
| **Magnetite** | #1E1E1E | 0.40 | 0.95 | Strong magnetic map | Separators, magnetic filters. |
| **Water** | #A0E7FF | 0.10 | 0.05 | Animated normal (ripples) | Tanks, cooling loops. |
| **Plastic** | #FBBF24 | 0.55 | 0.30 | Mold texture | Instrument enclosures, fittings. |
| **Glass** | #F8FAFF | 0.20 | 1.00 | High‑gloss normal | Sight glasses, observation ports. |

- All materials authored in **linear sRGB**; authoring tool (Blender) color space set to **Non‑Color** for roughness/metallic, **Color** for base color.
- Map textures at 2K resolution max; use NormalGL format for normal maps.
- Subsurface Scattering (SSS) optional for rubber‑lined and plastic only; keep shader complexity < 8 nodes.

## 7. Handoff Protocol

1. **Style Guide Reference** –
   - Every team (Intro Animation, 3D Digital Twin, 2D Diagrams, Unity Client) must cite the current version of this VISUAL_STYLE_GUIDE.md in their design documents (e.g., "Per VISUAL_STYLE_GUIDE.md §3, all UI transitions use cubic‑bezier…").
2. **Conflict Reporting** –
   - If a team identifies a visual clash (color, typography, animation timing, mesh budget), they must open an issue in the project tracker labeled **#visual‑conflict** and reference the specific section(s) of the guide.
   - The Visual Pipeline Architect (this role) will review within **48 hours** and either:
     a. Update the guide and broadcast the change, or
     b. Provide a justified exception with rationale.
3. **Versioning** –
   - Guide version follows `MAJOR.MINOR.PATCH` (e.g., `1.0.0`). Increment:
     - **MAJOR** on breaking visual changes (e.g., new primary color).
     - **MINOR** on additive additions (new material, extra LOD).
     - **PATCH** on typo/fix corrections.
   - All teams must pull the latest guide before sprint planning.
4. **Delivery Checklist** (per feature branch) –
   - [ ] Color palette usage verified against brand table.
   - [ ] Font sizes and weights match typography hierarchy.
   - [ ] Animation curves applied; timing documented.
   - [ ] GLB assets within LOD vertex budgets; Draco enabled.
   - [ ] PBR material assigned from library; texture maps included.
   - [ ] Intro sequence timed per §4; handoff to main UI noted.
   - [ ] Sign‑off: Team lead signs off in the branch PR description.

---
*Document generated by Visual Pipeline Architect, Arch SCADA. Version 1.0.0 – 2025‑08‑27.*
