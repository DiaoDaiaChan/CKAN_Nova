# Background Pack v2 · Agent Contract

## Goal

Add six new procedural background scenes and six matching theme skins to CKAN UI. Each pair is owned by one agent. The scenes must remain subtle wireframe/line-art layers that only show through page gaps, support slow motion and pointer response, and degrade cleanly when motion is disabled or high contrast is active.

## Pairing matrix

| # | Scene | Class / file | Palette | Palette file |
|---|-------|--------------|---------|--------------|
| 1 | 日冕 / Corona | CoronaScene.cs | 日冕金 / Corona Gold | docs/design/skins/corona-gold.json |
| 2 | 行星环 / Ring Plane | RingPlaneScene.cs | 月白石墨 / Lunar Graphite | docs/design/skins/lunar-graphite.json |
| 3 | 极光 / Aurora Oval | AuroraOvalScene.cs | 青瓷 / Celadon | docs/design/skins/celadon.json |
| 4 | 脉冲星 / Pulsar | PulsarScene.cs | 铜金 / Copper Instrument | docs/design/skins/copper.json |
| 5 | 太阳帆 / Solar Sail | SolarSailScene.cs | 星云紫 / Nebula Violet | docs/design/skins/nebula-violet.json |
| 6 | 太空电梯 / Space Elevator | SpaceElevatorScene.cs | 单色银 / Monochrome Silver | docs/design/skins/monochrome-silver.json |

All scene files live in src/CkanUI.App/Controls/Backgrounds/ and use namespace CkanUI.App.Controls.Backgrounds.

## Scene API contract

Implement:

    public sealed class <Name>Scene : IBackgroundSceneRenderer
    {
        public void Render(DrawingContext context, BackgroundSceneContext scene)
        {
            // draw only; no state, no timers, no resources
        }
    }

Read src/CkanUI.App/Controls/Backgrounds/IBackgroundSceneRenderer.cs and BackgroundSceneContext.cs before writing. Use only scene.Size/Width/Height/MinSide/Scale, scene.Time, scene.Intensity, scene.Influence, scene.InfluenceStrength, scene.MotionEnabled, scene.AccentAt(opacity), scene.TextAt(opacity). Do not resolve brushes from resources, do not create windows, do not touch settings, do not allocate per-frame large arrays.

Style rules:
- Low alpha: core lines <= 0.24 * intensity, ambient lines 0.04-0.10 * intensity.
- No filled shapes except a single very faint depth wash if essential; prefer strokes.
- Line widths scale with scene.Scale and clamp to roughly 0.6-2.4 DIP.
- Guard non-finite or tiny sizes; return early when Width/Height < 2.
- Motion must be driven by scene.Time with long periods (>= 17s) and no sudden jumps. If scene.MotionEnabled is false, freeze the time-driven phase but keep pointer response.
- Pointer response uses scene.Influence / scene.InfluenceStrength; when pointer is absent, influence strength is 0 and the scene still renders.
- No per-frame Random; deterministic from time and index.
- Keep composition away from page cards: use corners/edges and let the center breathe. Avoid a symmetric centered focal object.
- Do not edit shared registration files. The Lead will add BackgroundSceneKinds constants, renderer registrations, settings choices, localization, and tests.

## Scene briefs

### 1. Corona
Off-center total eclipse. Moon disk pressed into the upper-right corner, mostly off-canvas; 3-5 corona streamers spill inward, plus a tiny diamond-ring point. Streamers breathe on a 41s cycle; diamond ring shimmers on 7s. Pointer within 180 DIP bends streamers 2-4 DIP toward the pointer and brightens the diamond ring. Keep the moon face almost black; no filled sun disk. Risk: central composition would steal focus, so keep it at the corner and cap alpha.

### 2. Ring Plane
Planet limb in the lower-right, with three extremely thin ring lines crossing diagonally and one dark gap. Two shepherd moons on the ring. Ring precesses over 90s; moons orbit at 37s/53s. Pointer horizontal movement changes ring ellipticity from 0.18 to 0.26, giving a subtle perspective flip. Maximum three ring lines; do not draw a Saturn illustration. Risk: too many rings becomes heavy.

### 3. Aurora Oval
Magnetosphere arc in the lower-left; three aurora curtains hang along field lines, monochrome accent only, edges as polylines. Curtains sway 23s; field lines breathe 61s. Pointer in the upper half shifts the oval 3-6 DIP toward it and lifts local curtain brightness. No large gradient fills; that turns into a screensaver. Risk: color wash kills card readability.

### 4. Pulsar
Tiny neutron star at the upper-right; two opposite conical radio beams sweep every 3.7s; three concentric timing rings. Spin 3.7s, rings fade on 11s. Pointer shifts the next sweep phase by up to 12 degrees. Default visible period 4.8s, beam alpha <= 0.05. Risk: strong periodicity distracts; keep the sweep narrow and faint.

### 5. Solar Sail
Huge translucent sail in the upper-left: X-shaped booms, eight radial seams, one main tether; no spacecraft body. Sail breathes 17s with control points moving <=2 DIP; tether twists 43s. Pointer inside the sail projection creates a 4 DIP ripple at the nearest control point. Risk: overlaps the existing deep-space probe; subtract the probe, keep only sail and tether.

### 6. Space Elevator
Planet limb on the left; one very thin carbon-nanotube tether climbs off-canvas, a climber ascends, three tension rings at the anchor. Climber takes 90s to leave the top; tether sways 53s. Pointer horizontal movement bends the tether 2-3 DIP and speeds the climber by 1.4x. Keep the tether in the left quarter; exclude pages where the nav rail would collide. Risk: a vertical line cuts the layout.

## Palette JSON contract

Each palette file is UTF-8 JSON:

    {
      "key": "corona-gold",
      "displayName": "日冕金",
      "accent": "#E0B15C",
      "tokens": {
        "CkanCanvasBackgroundBrush": "#0D0B08",
        "CkanSurfaceBackgroundBrush": "#E615110C",
        "CkanSidebarBackgroundBrush": "#C01B1610",
        "CkanStatusBarBackgroundBrush": "#D91A150F",
        "CkanCardBackgroundBrush": "#D8211A12",
        "CkanElevatedCardHoverBrush": "#2B2218",
        "CkanRecessedInputBrush": "#0A0806",
        "CkanDialogBackgroundBrush": "#221B12",
        "CkanSubtleBackgroundBrush": "#C81F1912",
        "CkanMutedBackgroundBrush": "#2A2118",
        "CkanTextPrimaryBrush": "#F3E9D6",
        "CkanTextSecondaryBrush": "#B5A58B",
        "CkanTextTertiaryBrush": "#7E7261",
        "CkanBorderBrush": "#3A2E20",
        "CkanLayerElevatedBrush": "#2B2218",
        "CkanSkeletonBrush": "#30261B",
        "CkanAccentBrush": "#E0B15C",
        "CkanPrimaryBrush": "#E0B15C",
        "CkanAccentTextBrush": "#100C07"
      }
    }

Required token keys are exactly the 19 keys in the example. Values may be #RRGGBB or #AARRGGBB. Alpha surfaces should stay close to the existing dark theme (surface ~E6, sidebar ~C0, status ~D9, card ~D8, subtle ~C8). The Lead derives accent-subtle, navigation-selection, focus-ring, hover, and orbital-line from the accent plus the supplied surface colors.

Palette quality gate: text/canvas >= 7:1, secondary/canvas >= 4.5:1, accent/canvas >= 4.5:1, accentText/accent >= 4.5:1. If a proposed hex fails, adjust the accent or surface and record the ratios in the JSON as optional "contrast" fields.

## Integration owned by Lead

Lead will edit: src/CkanUI.Services.Abstractions/BackgroundSceneKinds.cs; src/CkanUI.App/Controls/Backgrounds/BackgroundSceneCanvas.cs; src/CkanUI.ViewModels/Pages/SettingsPageViewModel.cs; localization keys and packs; src/CkanUI.App/Resources/Tokens.axaml; src/CkanUI.App/ThemeHost.cs; src/CkanUI.App/Theming/AccentHost.cs if needed; tests and screenshots. Agents must not edit these files.

## Agent rules

- Only write files listed in your scope. Do not touch another agent's scene or palette.
- Do not run dotnet build or dotnet test while the team is active; the shared bin/obj locks. Lead runs the integration build.
- You may read any repository file. Use read/grep, not shell cat.
- Read the PUA skill before starting: C:/Users/43701/.dsh/profiles/tauri/node_modules/@michengai/dsh-pua/assets/pua/upstream/skills/pua/SKILL.md and its references/display-protocol.md. Follow the PUA behavior contract, including the diagnosis line before the first business edit, and use the 阿里 flavor because this is a construction task with delivery pressure.
- Final handoff must list: scene file, palette file, palette key/displayName, motion periods, pointer behavior, any risk, and what you did NOT verify.
