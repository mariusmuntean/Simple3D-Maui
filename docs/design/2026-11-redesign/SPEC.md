# Gallery redesign, November 2026

A redesign of every screen in the sample app (`samples/Simple3D.Demo`): gallery, the five scene viewers, the renderer menu, the GPU fallback state, all Mirror states, the wide layout, the app icon and the splash screen.

The look is warm and dark. Near-black surfaces, one glowing orange ("ember") for the GPU and for the main action on each screen, and a cool blue ("ice") only for the software fallback. Every screen has one main job and at most one primary button.

## What is in this folder

| Path | What it is |
|---|---|
| `screens/*.png` | Rendered reference for each screen, at 2× (phone screens are 390×844 pt, landscape Mirror 844×390, wide layout 1280×800). Implement against these. |
| `source/*.dc.html` | The design source of each screen: plain HTML with inline styles. Exact sizes, colors, radii, copy and accessibility labels are in here. |
| `source/canvas.json` | Screen list and order, used to render the PNGs. |
| `assets/` | Scene renders and character thumbnails used in the mockups. |

The live canvas these were exported from: https://claude.ai/artifact/FQpbSoHfaJ1bQ4Nu3xuJ7s (private to the owner).

Text in square brackets (`[n] fps`, `[device name]`, `[height]`, `[zoom]`) is a placeholder for the live value the app already computes. The "Live 3D character" box in the Mirror screens stands for the real `SceneView` output.

## Screens

| PNG | Screen | Today in code | Change |
|---|---|---|---|
| `Splash` | Splash | `Resources/Splash/splash.svg` on `#101A2D` | Warm cube on `#0A0908` (see App icon) |
| `Main` | Gallery | Header, renderer picker and scene buttons above one `SceneView` in `GalleryPage` | A grid of six equal cards (five scenes + Mirror), renderer chip top right. Tapping a card opens that scene's viewer |
| `Renderer` | Renderer menu | Segmented Auto/Software/Hardware + status label | Bottom sheet opened from the chip: three options with one-line explanations, then "Drawing now" with backend and diagnostic |
| `Fallback` | GPU fallback | Status label text only | Ice banner: "Drawing in software", the diagnostic, Dismiss and "Try GPU again" (re-applies the current mode, which already retries the GPU). The chip turns to "CPU" |
| `Equipment`, `Telemetry`, `Surface`, `RobotArm`, `Viewer` (Gantry Crane) | Scene viewer | Same page as the gallery | New full-screen viewer page per scene: back, title + lesson tag, renderer chip, frame-stats pill, camera rail (zoom in, zoom out, Fit, Reset), selection chip, a description card with Animate/Pause |
| `Surface` | Surface extras + stats open | "Shading: …" and "Light: …" toggle buttons | Two segmented controls, Shading (Smooth / Flat) and Light from (Left / Right), shown only for Surface. Also shows the frame-stats panel expanded |
| `RobotArm` | Nothing selected | "Tap an object to inspect it" | Dashed hint chip "Drag to orbit · tap a part to select it" |
| `MirrorIntro` | Mirror intro | Card with emoji rows | Character hero, three tiles (Whole body, Waist up, Face) with line icons, character picker, "Start camera", privacy line |
| `MirrorGuide` | Finding you | Full-screen preview, silhouette, guide pill | Same flow; ember dashed silhouette, shorter guide copy, icon buttons for close and switch camera |
| `MirrorLive` | Live | Character + camera inset + carousel | Same layout; camera inset shows "Tracking", character name + what it tracks above the carousel |
| `MirrorLost` | Lost + zoom | Lost hint pill, zoom pill | Same content restyled; inset shows "Searching" |
| `MirrorBlocked` | No camera | Message + Open Settings + Back | Adds the title "Camera is off" and an icon |
| `MirrorLandscape` | Live, landscape | Carousel moves to the left edge | Same rule, restyled; inset 192×108 top right |
| `Tablet` | Wide layout (tablet, Mac, Windows) | Same as phone | Sidebar (scenes, Mirror, renderer segmented control and status) + viewer |
| `System` | Tokens, icons, app icon, copy changes | — | Reference sheet |

The debug-only probe pages (`PoseProbePage`, `NativeGpuViewProbePage`, `LabelSyncProbePage`) and the debug stats line in Mirror are developer tools and are out of scope.

## Tokens

Put these in one place (for example `samples/Simple3D.Demo/Theme.cs`; the app builds its UI in C#) and use nothing else.

### Colors

| Token | Value | Use |
|---|---|---|
| Canvas | `#0A0908` | Page background, splash, app icon background |
| Surface | `#151210` | Cards, sheets, panels |
| Raised | `#1E1A17` | Controls on a surface |
| Line | `#2E2723` | 1 px borders, dividers |
| Text | `#F7F1EC` | Primary text and icons |
| Text soft | `#D6CBC2` | Descriptions |
| Text muted | `#A89C93` | Secondary labels (passes 4.5:1 on Surface) |
| Ember | `#FF8A3D` | GPU state, focus, selected borders, tags |
| Ember light | `#FFC08F` | Text on Ember tint |
| Ember tint | `#3A1C0A` | Selected backgrounds |
| On ember | `#2B1203` | Text and icons on Ember buttons |
| Ice | `#7CB7FF` | Software fallback only |
| Ice panel | `#0E1A2A`, border `#24405E`, text `#D6E8FF` / `#B9D2EE` | Fallback banner and CPU chip |
| Overlay | `rgba(21,18,16,.85)` | Controls floating over 3D content |
| Scrim | `rgba(5,4,3,.72)` | Behind sheets |

Gradients, only in two places:
- **Primary button:** linear, top to bottom, `#FFA45E` → `#FF7A2A`, with an ember glow (shadow `rgba(255,138,61,.45)`, radius 24, no offset).
- **Scene glow:** radial behind 3D content, `#3A1A08` at the center fading to `#140C07` (viewer) or `#0A0908` (pages). Use the same color for `SceneView.SceneBackgroundColor` at the edges (`#140C07`) so the 3D view blends in.

### Type

| Style | Font | Weight | Size (pt) |
|---|---|---|---|
| Display | Space Grotesk | 600 | 26–32 |
| Title | Space Grotesk | 600 | 18–22 |
| Label | Inter | 600 | 13–16 |
| Body | Inter | 400 | 13–15 |
| Data and tags | JetBrains Mono | 400–500 | 11–12 |

All three are OFL-licensed and on Google Fonts. Bundle the TTFs under `Resources/Fonts` and register them in `MauiProgram`.

### Shape and spacing

- Radii: 14 (small controls), 20 (cards), 24 (panels), 28 (sheets), full pill for chips and buttons.
- Spacing on a 4 pt grid; screen side margin 16; gaps 8–12 inside groups, 18–22 between groups.
- Every tappable control is at least 44×44 pt, including the clear-selection button and the stats pill.
- Floating controls over 3D content use Overlay with a 1 pt Line border.

## Icons

20 line icons, 24 pt grid, 2 pt stroke, round caps and joins. The SVG paths are in `source/System.dc.html` (and inline in each screen). Save each as an SVG in `Resources/Images` (MAUI rasterizes them) or build them as `Path` geometry. They replace the text glyphs and emoji used today:

| Today | Icon |
|---|---|
| `✕` | Close |
| `⇄` | Switch camera |
| `−` / `+` buttons | Zoom out / Zoom in |
| `Fit`, `Reset` buttons | Fit (corner brackets), Reset (counter-clockwise arrow) |
| `Mirror ✦` | Mirror card in the gallery grid |
| `🧍` `🙋` `🙂` | Whole body, Waist up, Face |

Other icons: Back, Animate (play), Pause, Frame stats (pulse line), Auto (bolt), Hardware (chip), Software (monitor), Start camera, Private (lock), Fallback (info circle), Settings.

## Components

| Component | Spec | MAUI building blocks |
|---|---|---|
| Renderer chip | 44 pt pill; 8 pt dot with glow + "GPU"; "CPU" in ice when the active backend is Software; dashed "Starting" before the first frame | `Border` + `Shadow` on the dot; tap opens the renderer sheet |
| Renderer sheet | Grabber, title, one sentence, three option rows (icon tile, name, one-line explanation, radio), "Drawing now" block with backend + diagnostic in mono | CommunityToolkit `Popup` anchored to the bottom, or a modal page with a scrim. MAUI has no built-in bottom sheet |
| Scene card | Image 118 pt tall, name, mono tag "01 · Primitives" | `Border` + `Image` + labels in a 2-column `Grid` |
| Camera rail | Vertical pill: Zoom in, Zoom out, divider, Fit, Reset | `VerticalStackLayout` of `ImageButton`s |
| Frame-stats pill | "idle" or "[n] fps"; tap expands to the four lines of today's HUD (fps, ms CPU · size, API, triangles) | Reuse `Hud.Update`; collapsed by default |
| Selection chip | Ember tint, "Selected" + shape name + 44 pt clear button; when empty, the dashed hint chip | Driven by `SceneView.SelectionChanged` |
| Description card | Scene description + primary Animate button that becomes an ember-tint "Pause" while running | Keep the button's `Text` as "Animate" / "Pause" (tests read it) |
| Segmented control | 3 pt inset track, selected segment in Ember tint with Ember light text | Two `Button`s or `RadioButton`s with a `ControlTemplate` |
| Primary button | 52–56 pt pill, ember gradient + glow, On-ember text | `Button` with `LinearGradientBrush` background + `Shadow` |
| Banner | Icon, title, one-sentence explanation, mono detail, text button + primary button | `Border` |

## Scene colors

The mockups show the scenes with their hues rotated 180°, so the structure turns warm and the old orange accents become cool blue highlights. To match, replace the colors in `samples/Simple3D.Shared`:

| Today | New |
|---|---|
| `0xFF0D1322` | `0xFF221C0D` |
| `0xFF18243B` | `0xFF3B2F18` |
| `0xFF30466C` | `0xFF6C5630` |
| `0xFF526B9A` | `0xFF9A8152` |
| `0xFF607AB1` | `0xFFB19760` |
| `0xFF76DBC7` | `0xFFDB768A` |
| `0xFF7894C8` | `0xFFC8AC78` |
| `0xFF80B2FF` | `0xFFFFCD80` |
| `0xFF8DA9FF` | `0xFFFFE38D` |
| `0xFFB7C6E2` | `0xFFE2D3B7` |
| `0xFFB7CEFF` | `0xFFFFE8B7` |
| `0xFFE4ECFF` (labels) | `0xFFFFF7E4` |
| `0xFFFFA66F` | `0xFF6FC8FF` |
| `0xFFFFBE79` | `0xFF79BAFF` |
| `0xFFFFC18B` | `0xFF8BC9FF` |

The Surface wave builds its vertex colors from a formula in `Surface.cs`; rotate those by 180° as well (a small hue-rotation helper in Shared is enough). Leave `MirrorStage`'s pond, island and mushrooms as they are, and change only the Mirror view's background to the warm canvas.

This is the riskiest change, because it touches the documentation images. Do it last, then regenerate them as `docs/DEVELOPMENT.md` describes (`--render-images --all` and `scripts/render-doc-images.py`).

## App icon and splash

The same isometric cube as today, recolored: top face `#FFC08F`, left `#FF8A3D`, right `#C2541A`, on a radial `#3A1A08` → `#0A0908` background. Drop the small sun dot. Set the `MauiIcon` and `MauiSplashScreen` colors in `Simple3D.Demo.csproj` to `#0A0908`. The SVG is in `source/Splash.dc.html` and `source/System.dc.html`.

## Copy

Every user-facing string is in the screen sources. The changes from today:

| Today | New |
|---|---|
| Gallery: "Simple3D" / "Interactive 3D for .NET MAUI" | Unchanged, smaller, beside the app mark |
| Scene buttons | Cards with name + tag: 01 · Primitives, 02 · Data, 03 · Meshes, 04 · Hierarchy, 05 · Linked motion, 06 · Camera (Mirror). The viewer uses the longer tags: "01 · Primitives & textures", "02 · Data to geometry", "03 · Meshes & shading" |
| "{Backend}: {diagnostic}" status line | Chip "GPU" / "CPU"; the full text moves to the sheet's "Drawing now" |
| "Shading: smooth" / "Shading: flat" | "Shading" + Smooth / Flat |
| "Light: from right" / "Light: from left" | "Light from" + Left / Right |
| "Selected: {name}" / "Tap an object to inspect it" | "Selected {name}" chip / "Drag to orbit · tap a part to select it" |
| "Drag to orbit · Pinch to zoom · Tap to select · Animate the scene" | Removed (covered by the empty-selection chip) |
| Scene caption "{Name} · {Description}" | Description only, in the card next to Animate (name is the page title) |
| Mirror: "Your character: {name} · change it any time" | "Character" + name above the picker |
| "Everything runs on this device. Nothing is recorded or sent." | "Runs on this device. Nothing is recorded or sent." |
| "Get into the frame: your whole body, from the waist up, or just your head for face tracking" | "Get into the frame: whole body, waist up, or just your head for face tracking" |
| Camera blocked messages | Same messages, with the title "Camera is off" above them |
| Fallback (new) | "Drawing in software" / "The GPU stopped, so Auto switched to the CPU. Everything still works." / Dismiss / Try GPU again |
| Renderer sheet (new) | Auto "GPU when it works, software when it doesn't." · Hardware "GPU only: Metal or OpenGL ES. Blank if the GPU fails." · Software "Drawn on the CPU with SkiaSharp. Works everywhere." |

Keep every `SemanticProperties` description and hint that exists today, and give every icon-only button a description (the screen sources carry them as `aria-label`).

## Things the tests rely on

`tests/Simple3D.Maui.Tests` drives the gallery headlessly. Keep these working, or change the tests in the same PR:

- `AutomationId`s `RenderingMode` (with `RenderingModeAuto` / `RenderingModeSoftware` / `RenderingModeHardware`), `RenderingBackend` and `FrameStats`. Put the first two on the renderer sheet's options and status.
- `SurfaceShading` and `SurfaceLight`. They become segmented controls, so the tests that read `"Light: from right"` and `"smooth"` from button text need updating.
- Scene buttons found by scene name, and the Animate button found by text "Animate" / "Pause".
- `GalleryPage` is created directly by the tests. When the viewer moves to its own page, update `Gallery()` in `DemoAppearanceTests` and `RunGallery()` in `RenderingModeTests` to open it.

## Suggested order

1. Theme, fonts and icons. No layout changes.
2. Gallery grid + `SceneViewerPage` (rail, stats pill, selection chip, description card, Surface controls).
3. Renderer chip, sheet and fallback banner.
4. Mirror restyle.
5. Wide layout (sidebar + viewer above a width breakpoint, about 840 pt).
6. App icon and splash, then scene colors and regenerated documentation images.
