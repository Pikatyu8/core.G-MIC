# Third-Party Notices and Licensing

## Repository License Overview

Unless explicitly noted otherwise below, the source code, shaders, and G'MIC-derived ports in **core.G'MIC** are licensed under the **CeCILL Free Software License Agreement v2.1** (see `LICENSE-CeCILL_V2.1` in the repository root, or <https://cecill.info/licences/Licence_CeCILL_V2.1-en.html>).

### License Exception: Halftone Shapes (`CC BY-NC-SA 3.0 NZ`)

The following files are ported from Iain Fergusson's `iain_halftone_shapes` filter (`gmic-community/include/iain_fergusson.gmic`) and are **excluded from the CeCILL v2.1 license**. They remain governed by the **Creative Commons Attribution-NonCommercial-ShareAlike 3.0 New Zealand** license (<http://creativecommons.org/licenses/by-nc-sa/3.0/nz/>):

- `src/Heavy/HalftoneShapesFilter.cs`
- `CoreGMIC_ShaderBuilder/Assets/Shaders/GmicHalftoneShapes.shader`

Because of the NonCommercial (NC) and ShareAlike (SA) terms of CC BY-NC-SA 3.0 NZ, builds and distributions containing these two files are strictly **non-commercial**, and modifications to these two files must remain under CC BY-NC-SA 3.0 NZ with attribution to **Iain Fergusson** (<http://iainfergusson.co.nz>).

---

## Attributions and Ported Works

### 1. G'MIC Standard Library (`GreycLab/gmic`)
- **Upstream:** <https://gmic.eu> / <https://github.com/GreycLab/gmic>
- **Copyright:** David Tschumperlé — GREYC UMR CNRS 6072, Image Team
- **Upstream License:** Dual-licensed under **CeCILL-C v1** or **CeCILL v2.1** (used here under **CeCILL v2.1**)
- **Derived/Ported Components (rewritten in C# / HLSL / Compute Shaders for Unity):**
  - `fx_array_color` (`ArrayColorFilter.cs`, `GmicArrayColor.shader`)
  - `fx_cubism` / `cubism` (`CubismFilter.cs`, `GmicCubism.shader`, `GmicCubismStep.compute`)
  - `fx_cutout` (`CutoutFilter.cs`, `GmicCutout.shader`, `GmicCutout.compute`)
  - `fx_edges` (`EdgesFilter.cs`, `GmicEdges.shader`)
  - `fx_fire_edges` / `fire_edges` (`FireEdgesFilter.cs`, `GmicFireEdges.shader`)
  - `fx_frame_cube` / `frame_cube` (`FrameCubeFilter.cs`, `GmicFrameCube.shader`)
  - `fx_gradient_norm` (`GradientNormFilter.cs`, `GmicGradientNorm.shader`)
  - `fx_imageobject3d` (`ImageObject3DFilter.cs`, `ImageObject3DMeshBuilder.cs`, `GmicImageObject3D.shader`)
  - `fx_isophotes` / `isophotes` (`IsophotesFilter.cs`, `GmicIsophotes.shader`)
  - `fx_lava` (`LavaFilter.cs`, `GmicLava.shader`) and `_palette_hot` LUT data (`GmicPaletteLoader.cs`)
  - `fx_8bits` (`Oldschool8BitsFilter.cs`, `Gmic8Bits.shader`)
  - `fx_pack_ellipses` (`PackEllipsesFilter.cs`, `GmicPackEllipses.shader`, `GmicPackEllipses.compute`)
  - `fx_quadtree` (`QuadtreeVariationsFilter.cs`, `GpuQuadtree.cs`, `GmicQuadtree.shader`)
  - `fx_stencil` (`StencilFilter.cs`, `GmicStencil.shader`)
  - `fx_vector_painting` (`VectorPaintingFilter.cs`, `GmicVectorPainting.shader`)
  - Shared GPU equivalents of G'MIC primitives: `shapeaverage0` (`GpuShapeAverage.cs`), `watershed` (`GpuWatershed.cs`), `plasma` (`PlasmaGenerator.cs`), `smooth_anisotropic`, `normalize_local`, and color space transforms (`GpuOps.cs`, `GmicOps.shader`, `GmicColorSpaces.cginc`).

### 2. G'MIC Community Filters (`GreycLab/gmic-community`)
- **Upstream:** <https://github.com/GreycLab/gmic-community>
- **Joan Rake (`joan_rake.gmic`, Copyright David Tschumperlé, CeCILL v2.0):**
  - `fx_ultrawarp4plus` (`UltraWarp4PlusFilter.cs`, `GmicUltraWarp4Plus.shader`)
- **Sylvie Alexandre / samj (`sylvie_alexandre.gmic`, Free Software / CeCILL v2.0+):**
  - `samj_Barbouillage_Paint_Daub` (`BarbouillagePaintDaubFilter.cs`, `GmicBarbouillage.shader`)
  - `samj_chalkitup` (`ChalkItUpFilter.cs`, `GmicChalkItUp.shader`, originally inspired by Lylejk's tutorial)
- **Iain Fergusson (`iain_fergusson.gmic`, Copyright Iain Fergusson, CC BY-NC-SA 3.0 NZ):**
  - `iain_halftone_shapes` (`HalftoneShapesFilter.cs`, `GmicHalftoneShapes.shader`)

### 3. Hollow Knight Modding Dependencies (External References, Not Bundled)
- **Satchel** (<https://github.com/PrashantMohta/Satchel>) — **MIT License**. Referenced externally (`<Private>false</Private>`).
- **Hollow Knight & Modding API assemblies** — Referenced externally for compilation only (`<Private>false</Private>`) and are not redistributed in this repository.

---

## Warranty and Liability Notice (CeCILL Articles 8 & 9)

This software is provided on an "AS IS" basis without warranty of any kind, either expressed or implied, including without limitation warranties that the software is free of defects, merchantable, fit for a particular purpose, or non-infringing. The entire risk as to the quality and performance of the software is with the user. In no event shall the authors, copyright holders, or contributors be liable for any direct, indirect, incidental, special, exemplary, or consequential damages arising from the use or inability to use this software.