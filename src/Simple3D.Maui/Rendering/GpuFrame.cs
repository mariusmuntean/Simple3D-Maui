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
            Selection = new(ReferenceEquals(item.Shape, frame.Selected) ? 1 : 0, item.Shape.Material.DoubleSided ? 1 : 0, 0, 0)
        };
    }
}

internal static class GpuShaders
{
    internal const string SelectionMaskDefine = "SELECTION_MASK";
    internal const string GlslVertex = """
        #version 300 es
        precision highp float;
        layout(location=0) in vec4 position;
        layout(location=1) in vec4 normal;
        layout(location=2) in vec4 uv;
        layout(location=3) in vec4 color;
        layout(std140) uniform Params {
            mat4 world; mat4 vp; mat4 normals;
            vec4 lightAmbient; vec4 lightColor; vec4 tintLit; vec4 selection;
        };
        out vec2 texCoord;
        out vec3 frontColor;
        out vec3 backColor;
        flat out float selectedFlag;
        void main() {
            vec4 p=world*position;
            gl_Position=vp*p;
            gl_Position.z=2.0*gl_Position.z-gl_Position.w;
            vec3 n=normalize((normals*normal).xyz);
            vec3 litFront=vec3(lightAmbient.w)+lightColor.rgb*lightColor.w*max(0.0,dot(n,-lightAmbient.xyz));
            vec3 litBack=vec3(lightAmbient.w)+lightColor.rgb*lightColor.w*max(0.0,dot(-n,-lightAmbient.xyz));
            frontColor=color.rgb*tintLit.rgb*mix(vec3(1),litFront,tintLit.w);
            backColor=color.rgb*tintLit.rgb*mix(vec3(1),litBack,tintLit.w);
            texCoord=vec2(uv.x,1.0-uv.y);
            selectedFlag=selection.x;
        }
        """;
    // Compiled twice: with SELECTION_MASK for the offscreen mask/outline path, and without it for
    // direct rendering to the window surface when no selection is visible.
    internal const string GlslFragment = """
        #version 300 es
        precision highp float;
        in vec2 texCoord; in vec3 frontColor; in vec3 backColor; flat in float selectedFlag;
        uniform sampler2D image;
        layout(location=0) out vec4 outputColor;
        #ifdef SELECTION_MASK
        layout(location=1) out vec4 outputMask;
        #endif
        void main() {
            outputColor=vec4(clamp((gl_FrontFacing?frontColor:backColor)*texture(image,texCoord).rgb,0.0,1.0),1);
        #ifdef SELECTION_MASK
            outputMask=vec4(selectedFlag,0,0,1);
        #endif
        }
        """;
    internal const string GlslScreenVertex = """
        #version 300 es
        precision highp float;
        out vec2 texCoord;
        void main() { vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2); texCoord=p; gl_Position=vec4(p*2.0-1.0,0,1); }
        """;
    // Color is sampled with linear filtering so a capped render size upscales smoothly; the mask
    // is fetched exactly. The outline covers background pixels within outlineRadius mask texels.
    internal const string GlslScreenFragment = """
        #version 300 es
        precision highp float;
        in vec2 texCoord;
        uniform sampler2D sceneColor; uniform sampler2D sceneMask; uniform int outlineRadius;
        out vec4 outputColor;
        void main() {
            vec3 c=texture(sceneColor,texCoord).rgb;
            ivec2 size=textureSize(sceneMask,0); ivec2 hi=size-1; ivec2 p=clamp(ivec2(texCoord*vec2(size)),ivec2(0),hi);
            float m=texelFetch(sceneMask,p,0).r;
            float neighbor=0.0;
            for(int r=1;r<=3;r++) {
                if(r>outlineRadius) break;
                neighbor=max(neighbor,texelFetch(sceneMask,clamp(p+ivec2(r,0),ivec2(0),hi),0).r);
                neighbor=max(neighbor,texelFetch(sceneMask,clamp(p-ivec2(r,0),ivec2(0),hi),0).r);
                neighbor=max(neighbor,texelFetch(sceneMask,clamp(p+ivec2(0,r),ivec2(0),hi),0).r);
                neighbor=max(neighbor,texelFetch(sceneMask,clamp(p-ivec2(0,r),ivec2(0),hi),0).r);
            }
            if(m>0.5) c=mix(c,vec3(0.3,0.7,1.0),0.25);
            else if(neighbor>0.5) c=vec3(0.3,0.7,1.0);
            outputColor=vec4(c,1);
        }
        """;
    internal static string WithDefine(string source, string define)
    {
        var lineEnd = source.IndexOf('\n');
        return source[..(lineEnd + 1)] + "#define " + define + " 1\n" + source[(lineEnd + 1)..];
    }
    internal const string Metal = """
        #include <metal_stdlib>
        using namespace metal;
        struct Vertex { float4 position, normal, uv, color; };
        struct Params { float4x4 world, vp, normals; float4 lightAmbient, lightColor, tintLit, selection; };
        struct Varying { float4 position [[position]]; float2 uv; float3 frontColor, backColor; float selected [[flat]]; };
        struct Output { float4 color [[color(0)]]; float4 mask [[color(1)]]; };
        vertex Varying sceneVertex(const device Vertex* vertices [[buffer(0)]], constant Params& p [[buffer(1)]], uint id [[vertex_id]]) {
            Vertex v=vertices[id]; Varying o;
            o.position=p.vp*p.world*v.position;
            float3 n=normalize((p.normals*v.normal).xyz);
            float3 f=float3(p.lightAmbient.w)+p.lightColor.rgb*p.lightColor.w*max(0.0,dot(n,-p.lightAmbient.xyz));
            float3 b=float3(p.lightAmbient.w)+p.lightColor.rgb*p.lightColor.w*max(0.0,dot(-n,-p.lightAmbient.xyz));
            o.frontColor=v.color.rgb*p.tintLit.rgb*mix(float3(1),f,p.tintLit.w);
            o.backColor=v.color.rgb*p.tintLit.rgb*mix(float3(1),b,p.tintLit.w);
            o.uv=float2(v.uv.x,1-v.uv.y); o.selected=p.selection.x; return o;
        }
        static float4 shade(Varying v, bool front, texture2d<float> image, sampler s) {
            return float4(clamp((front?v.frontColor:v.backColor)*image.sample(s,v.uv).rgb,0.0,1.0),1);
        }
        fragment Output sceneFragment(Varying v [[stage_in]], bool front [[front_facing]],
            texture2d<float> image [[texture(0)]], sampler imageSampler [[sampler(0)]]) {
            Output o; o.color=shade(v,front,image,imageSampler); o.mask=float4(v.selected,0,0,1); return o;
        }
        fragment float4 sceneFragmentDirect(Varying v [[stage_in]], bool front [[front_facing]],
            texture2d<float> image [[texture(0)]], sampler imageSampler [[sampler(0)]]) {
            return shade(v,front,image,imageSampler);
        }
        struct Screen { float4 position [[position]]; float2 uv; };
        vertex Screen screenVertex(uint id [[vertex_id]]) {
            float2 p=float2((id<<1)&2,id&2); Screen o; o.position=float4(p*2-1,0,1); o.uv=float2(p.x,1-p.y); return o;
        }
        fragment float4 screenFragment(Screen v [[stage_in]], texture2d<float> color [[texture(0)]], texture2d<float> mask [[texture(1)]],
            constant int& radius [[buffer(0)]]) {
            constexpr sampler s(filter::linear, address::clamp_to_edge);
            float3 c=color.sample(s,v.uv).rgb;
            int2 hi=int2(mask.get_width()-1,mask.get_height()-1); int2 p=clamp(int2(v.uv*float2(mask.get_width(),mask.get_height())),int2(0),hi);
            float m=mask.read(uint2(p)).r;
            float n=0;
            for(int r=1;r<=min(radius,3);r++)
                n=max(n,max(max(mask.read(uint2(clamp(p+int2(r,0),int2(0),hi))).r,mask.read(uint2(clamp(p-int2(r,0),int2(0),hi))).r),
                            max(mask.read(uint2(clamp(p+int2(0,r),int2(0),hi))).r,mask.read(uint2(clamp(p-int2(0,r),int2(0),hi))).r)));
            if(m>0.5)c=mix(c,float3(0.3,0.7,1),0.25); else if(n>0.5)c=float3(0.3,0.7,1);
            return float4(c,1);
        }
        """;
}
