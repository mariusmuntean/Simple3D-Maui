// Compiled for iOS and Mac Catalyst only; see Simple3D.Maui.csproj.
namespace Simple3D.Maui;

internal static class MetalShaders
{
    internal const string Source = """
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
