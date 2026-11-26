# Product Guidelines: Arch SCADA

## Industrial UX & Visual Quality Standards
- **High-Contrast Control Room Ergonomics:** Dark-first industrial UI with clear ISA-18.2 alarm color semantics (Fault = Red `#EF4444`, Warning/Starved = Amber `#F59E0B`, Running = Emerald `#22C55E`, Offline/Stopped = Slate `#64748B`).
- **3D Visual Fidelity & Performance Budgets:**
  - Cold load of all 24 `.glb` assets must complete in `< 2000 ms`.
  - Per-frame coupled process flow and wheel-loader kinematics evaluation must stay `< 500 µs`.
  - Hardware-accelerated frame draw calls must remain `< 100` via static VBO merging and hardware instancing.
- **Cross-Runtime GLB Compatibility:** Every `.glb` in `ArchScada/Assets/3D/Exported/` must load cleanly in both `SharpGLTF` / `StbImageSharp` (`ArchScada.Desktop`) and Three.js `GLTFLoader` (`ScadaWeb`).
