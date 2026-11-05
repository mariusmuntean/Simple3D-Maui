using System.Numerics;
using System.Runtime.InteropServices;
using Simple3D.Core;

namespace Simple3D.Maui;

// OutlineRadius: Selection outline width in render pixels (1-3), chosen so the outline stays visible on high-density screens.
internal sealed record GpuFrame(GpuScene Geometry, Scene Scene, Camera Camera, Shape? Selected, uint Background, int OutlineRadius = 1)
{
    /// <summary>True when the selected shape is drawn this frame, which requires the mask and outline composite.</summary>
    internal bool HasVisibleSelection
    {
        get
        {
            if (Selected is null) return false;
            var items = Geometry.Items;
            for (var i = 0; i < items.Count; i++) if (ReferenceEquals(items[i].Shape, Selected)) return true;
            return false;
        }
    }
    /// <summary>One pass straight to the output suffices when nothing is outlined and nothing is upscaled.</summary>
    internal bool CanRenderDirect(int width, int height, int outputWidth, int outputHeight) =>
        width == outputWidth && height == outputHeight && !HasVisibleSelection;
    // The single place the outline radius is limited; renderers and shaders pass it through.
    internal static int ClampOutlineRadius(double renderPixelsPerDip) =>
        double.IsFinite(renderPixelsPerDip) ? Math.Clamp((int)Math.Round(renderPixelsPerDip), 1, 3) : 1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuUniforms
{
    internal Matrix4x4 World, ViewProjection, Normal;
    internal Vector4 LightAmbient, LightColor, TintLit, Selection;
    internal Vector4 MorphIndices0, MorphIndices1, MorphWeights0, MorphWeights1, MorphInfo;
    internal static GpuUniforms Create(GpuFrame frame, GpuItem item, in Matrix4x4 viewProjection)
    {
        var tint = GpuGeometry.Rgba(item.Shape.Color);
        var light = frame.Scene.Light;
        return new()
        {
            World = item.World,
            ViewProjection = viewProjection,
            Normal = item.Normal,
            LightAmbient = new(light?.Direction ?? Vector3.UnitZ, frame.Scene.AmbientLight),
            LightColor = new(light?.Rgb ?? Vector3.Zero, light?.Intensity ?? 0),
            TintLit = new(tint.X, tint.Y, tint.Z, item.Shape.Material.Lit ? 1 : 0),
            MorphIndices0 = item.Morphs.Indices0, MorphIndices1 = item.Morphs.Indices1,
            MorphWeights0 = item.Morphs.Weights0, MorphWeights1 = item.Morphs.Weights1,
            MorphInfo = new(item.Geometry.MorphVertexCount, item.Morphs.Count, 0, 0),
            Selection = new(ReferenceEquals(item.Shape, frame.Selected) ? 1 : 0, item.Shape.Material.DoubleSided ? 1 : 0, 0, 0)
        };
    }
}
