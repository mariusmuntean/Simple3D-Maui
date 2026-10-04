# Presentation assets

Shared icons, documentation banner and social preview image. The banner and social card describe Simple3D as a 3D software renderer.

| Asset | Use |
| --- | --- |
| `mark.svg` | Transparent symbol for dark backgrounds |
| `package-icon.svg` and `package-icon.png` | Library package icon |
| `../../samples/Simple3D.Demo/Resources/AppIcon/` | Demo app icon background and foreground |
| `../../samples/Simple3D.Demo/Resources/Splash/splash.svg` | Demo splash mark |
| `../../docs/site/images/brand-banner.svg` | GitHub README and documentation header |
| `social-card.svg` and `social-card.png` | 1200 × 630 social preview artwork |

Palette: midnight `#101A2D`, top `#BED0FF`, left `#7E98F4`, right `#415ED3`, light `#FFC47A`, accent `#77DDCE`.

The PNGs are rendered from their SVG sources with `rsvg-convert`:

```bash
rsvg-convert -w 256 -h 256 assets/brand/package-icon.svg -o assets/brand/package-icon.png
rsvg-convert assets/brand/social-card.svg -o assets/brand/social-card.png
```
