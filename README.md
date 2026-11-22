# core.G'MIC

A Hollow Knight post-processing mod that brings real-time GPU ports of [G'MIC](https://gmic.eu) image filters, color LUTs, and stylization effects directly onto the game's main camera.

---

## Features

- **25 Real-Time GPU Filters** ported to Unity HLSL shaders and Compute Shaders (with `0` native C++ G'MIC runtime dependencies).
- **Per-Filter Parameter Menus** with dynamic visibility, step snapping, and instant reset to defaults.
- **Slider Animation Mode** for any numeric parameter:
  - Per-slider **Loop Mode**: `Oscillate` (smooth sine wave), `Infinite Loop` (seamless wrap from Min to Max), and `Reverse Loop` (Max to Min).
  - Configurable **Frequency (Hz)**, **Anim Min / Max** bounds, and **Synchronize Freq** across animated sliders.
- **Entity Exclusions** via an overlay camera between `mainCamera` and `hudCamera` so excluded sprites render cleanly without post-processing:
  - Built-in toggles for **Disable Player** (Knight + nail/nail-art slashes) and **Disable Tiktik**.
  - Custom JSON rules in `exclusions.json` (next to `core.GMIC.dll`) with **Open Exclusion Settings** and **Reload Exclusions** buttons directly in the mod menu.
- **Extra Side-Panel Controls:**
  - **No FastNoise** — disables Hollow Knight's built-in screen grain pass.
  - **Experimental Shaders** — unlocks experimental filters in the filter selector.
- **Main Menu Safe** — post-processing automatically bypasses `MAIN_MENU` so menus stay readable.

---

## Included Filters

### Color LUT
- **Trigonometry LUT** — RGB channel mapping via `|sin|`, `|tan|`, or `|sec| - 1` oscillations.

### Standard
- **Pixelate** — Spatial `N×N` pixel block averaging.
- **Cubism** — Randomized rotated textured squares with optional correlated random-walk animation (`Max Step`, `No-Repeat Steps`, `Direction Persistence`).
- **Cutout** — Paper-cutout color quantization via GPU K-Means, connected-component area filtering, and Jump Flooding (JFA) hole inpainting.
- **Array [Random Colors]** — Grid overlay of randomized colored tiles (static or animated, uniform or random tile sizes).
- **Edges** — Gradient-norm edge extraction using JFA Euclidean distance and GPU histogram equalization.
- **Gradient Norm** — Magnitude of image gradients with linearity curve and percentile cuts.
- **Isophotes** — Brightness contour lines (`Transparent`) or region-averaged topographic bands (`Colors`).
- **Player Swirl** — Aspect-corrected swirl centered on the Knight or screen center (`dist^2`, `dist^8 * 0.1`, or custom power).
- **Frame [Cube]** — Analytic 3D perspective tunnel mapping the frame onto inner cube faces.
- **Halftone Shapes** — Patterned halftone screening (`Circle`, `Star`, `Triangle`, `Heart`) with 4×4 oversampling, rotation, and twirl.
- **3D Image Object** — Maps the frame onto shaded 3D geometry (`Plane`, `Cube`, `Pyramid`, `Sphere`, `Torus`, `Gyroid`, `Weird`, `Cup`, `Rubik`) with `Dots`, `Wireframe`, `Flat`, `Flat-Shaded`, `Gouraud`, and `Phong` rendering.
- **Edges on Fire** — Real-time animated flame simulation rising from image edges.
- **CRT Sub-Pixels** — 24×14 RGB phosphor sub-pixel lattice with mean-curvature flow, anisotropic phosphor blur, optional screen border, and histogram equalization.
- **Noise [Additive]** — `Gaussian`, `Uniform`, `Salt and Pepper`, and `Poisson` noise in any supported color space.
- **Warp by Intensity** — Displaces pixels based on their own channel or Euclidean norm intensity in any supported color space.

### GPU Heavy
- **Barbouillage Daub** — Painterly engraving, GPU histogram equalization, and anisotropic paint-daub smoothing with optional fractal plasma HSL remix.
- **Chalk It Up** — Local luminance normalization, anisotropic smoothing, CIE Lab chroma boost, JFA watershed segmentation, morphology, and chalk spread.
- **UltraWarp++++** — Multi-iteration randomized pipeline combining plasma, watershed, quadtree, noise, intensity-warp, negation, and HSV scaling.
- **Pack Ellipses** — GPU SLIC contour analysis and multi-round conflict-free ellipse packing rendered via `DrawProceduralIndirect`.
- **Lava** — Fractal plasma modulated by image norm, anisotropic flow, histogram equalization, and `hot` colormap lookup.
- **Stencil** — Iterative unsharp-mask contrast stencil in `Black & White`, `RGB`, or shape-averaged `Color` modes.

### Experimental *(enable via side panel)*
- **Quadtree Variations** — Adaptive GPU quadtree decomposition (`Squares`, `Sierpinski Design`, `Ellipse Painting`).
- **Vector Painting** — Gradient-direction region segmentation with coherent temporal IIR stabilization.
- **Oldschool 8bits** — Downscaled Otsu-dithered connected-component color quantization.

---

## Requirements & Installation

### Requirements
- **Hollow Knight** (1.5.78+) with the **Modding API**
- **Satchel** (installed alongside `core.G'MIC`)
- A GPU supporting **DirectX 11 / Shader Model 4.5+** and **Compute Shaders** (filters requiring compute shaders will show `[N/A]` if unsupported)

### Installation
1. Install via **Lumafly** / **Scarab** *(recommended)*, or download the latest release `.zip` from GitHub Releases.
2. Ensure **Satchel** is installed.
3. Copy the contents of the release archive (`core.GMIC.dll`, `LICENSE`, `THIRD-PARTY-NOTICES.md`) into:
   `<Hollow Knight>/hollow_knight_Data/Managed/Mods/core.GMIC/`

---

## Custom Entity Exclusions (`exclusions.json`)

An `exclusions.json` file is generated automatically in the mod folder (`Managed/Mods/core.GMIC/exclusions.json`) on first run.

1. Open the `core.G'MIC` mod menu and click **Open Exclusion Settings** (in the bottom-right corner).
2. Copy an entry from `_Examples` into `Rules` and customize `ExactNames`, `NameContains`, `SceneContains`, `IncludeChildren`, and `PollIntervalSeconds`.
3. Save `exclusions.json` and click **Reload Exclusions** in-game.

---

## Building from Source

### Prerequisites
- .NET SDK (targeting `net472`)
- Unity **2020.2.2f1** (only needed when modifying `.shader` / `.compute` / `.cginc` files)
- Hollow Knight + Modding API + Satchel assemblies

### 1. Rebuild the Shader AssetBundle *(if shaders were modified)*
1. Open `CoreGMIC_ShaderBuilder/` in **Unity 2020.2.2f1**.
2. Run menu item **`CoreGMIC -> Build Windows Shader Bundle`**.
3. Copy `CoreGMIC_ShaderBuilder/Assets/BundleOutput/win/gmicshaders` to `Assets/Build/win/gmicshaders`.

### 2. Configure Local Paths & Compile
1. Copy `LocalBuildProperties_example.props` to `LocalBuildProperties.props` and set your `ModdingAPI`, `VanillaFiles`, and `HollowKnightModsDirectory` paths.
2. Validate shaders & build the mod:
   ```bash
   dotnet build CoreGMIC_ShaderBuilder/CoreGMIC_ShaderBuilder.csproj
   dotnet build core.GMIC.csproj
   ```

---

## Licensing & Credits

- Unless noted otherwise, **core.G'MIC** is distributed under the **CeCILL Free Software License Agreement v2.1** — see [`LICENSE`](LICENSE-CeCILL_V2.1).
- **Non-Commercial Exception:** `src/Heavy/HalftoneShapesFilter.cs` and `CoreGMIC_ShaderBuilder/Assets/Shaders/GmicHalftoneShapes.shader` are ported from Iain Fergusson's `iain_halftone_shapes` (`gmic-community`) and remain under **CC BY-NC-SA 3.0 NZ** (strictly non-commercial).
- Full third-party copyright notices, upstream attributions (David Tschumperlé / GREYC CNRS, Joan Rake, Sylvie Alexandre / `samj`, Andy Kelday, Iain Fergusson, Satchel), and CeCILL warranty disclaimers are listed in [`THIRD-PARTY-NOTICES.md`](LICENSE-THIRD-PARTY-NOTICES.md).
- Source code is publicly available at [Pikatyu8/core.G-MIC](https://github.com/Pikatyu8/core.G-MIC) in accordance with CeCILL v2.1 source disclosure terms.