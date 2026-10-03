# Announcement draft

I built Simple3D, a small 3D library for .NET MAUI.

I wanted to put equipment diagrams, data displays and scientific models inside ordinary MAUI pages. You describe a scene in C# using shapes or indexed meshes, then display it in a SceneView. Users can orbit, zoom and select parts. Selection stays visible as those parts animate.

The gallery has fifteen examples: a moving robot arm, animated telemetry bars, a solar tracker, network traffic, a survey drone and more. It runs on iOS, Android and macOS through Mac Catalyst.

The renderer uses CPU depth rendering and fixed directional lighting. I kept the scope small: opaque illustrations with picking and simple animation. Textures, transparency and shadows are outside its current capabilities.

The source is available under MIT. I'd welcome feedback from MAUI developers, particularly small visualizations you'd like to put in your apps.

Documentation: https://mariusmuntean.github.io/Simple3D-Maui/
Source and demo: https://github.com/mariusmuntean/Simple3D-Maui

#dotnet #MAUI #OpenSource #DataVisualization

## Capture before posting

Record a short gallery clip on a physical device: Telemetry height/color animation, selection following the Equipment arrow, then orbit and pinch. Use the existing `assets/brand/social-card.png` for a still image. Avoid claiming 60/120 FPS without a displayed-frame measurement. Packages are prepared as preview builds; this task does not publish them to NuGet.
