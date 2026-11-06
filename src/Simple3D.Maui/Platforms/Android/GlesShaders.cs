// Compiled for Android only (and linked by the desktop GLES tests); see Simple3D.Maui.csproj.
namespace Simple3D.Maui;

internal static class GlesShaders
{
    internal const string SelectionMaskDefine = "SELECTION_MASK";
    internal const string SkinnedDefine = "SKINNED";
    /// <summary>Joint matrices per skinned draw; the std140 block is 64 bytes per joint.</summary>
    internal const int PaletteBytes = Simple3D.Core.Skeleton.MaximumJoints * 64;
    internal const string GlslVertex = """
        #version 300 es
        precision highp float;
        layout(location=0) in vec4 position;
        layout(location=1) in vec4 normal;
        layout(location=2) in vec4 uv;
        layout(location=3) in vec4 color;
        #ifdef SKINNED
        layout(location=4) in vec4 jointIndices;
        layout(location=5) in vec4 jointWeights;
        layout(location=6) in vec4 sourceVertex;
        uniform highp sampler2D morphDeltas;
        layout(std140) uniform Joints { mat4 joints[128]; };
        #endif
        layout(std140) uniform Params {
            mat4 world; mat4 vp; mat4 normals;
            vec4 lightAmbient; vec4 lightColor; vec4 tintLit; vec4 selection;
            vec4 morphIndices0; vec4 morphIndices1; vec4 morphWeights0; vec4 morphWeights1; vec4 morphInfo;
        };
        out vec2 texCoord;
        out vec3 frontColor;
        out vec3 backColor;
        flat out float selectedFlag;
        void main() {
            vec4 localPosition=position; vec3 localNormal=normal.xyz;
        #ifdef SKINNED
            int width=textureSize(morphDeltas,0).x;
            for(int i=0;i<int(morphInfo.y);i++) {
                int lane=i&3;
                int target=int(i<4?morphIndices0[lane]:morphIndices1[lane]);
                float weight=i<4?morphWeights0[lane]:morphWeights1[lane];
                int texel=2*(target*int(morphInfo.x)+int(sourceVertex.x));
                localPosition.xyz+=weight*texelFetch(morphDeltas,ivec2(texel%width,texel/width),0).xyz;
                texel++;
                localNormal+=weight*texelFetch(morphDeltas,ivec2(texel%width,texel/width),0).xyz;
            }
            ivec4 j=ivec4(jointIndices);
            mat4 skin=joints[j.x]*jointWeights.x+joints[j.y]*jointWeights.y+joints[j.z]*jointWeights.z+joints[j.w]*jointWeights.w;
            localPosition=skin*localPosition; localNormal=(skin*vec4(localNormal,0)).xyz;
            float normalLength=length(localNormal);
            localNormal=normalLength>0.0&&!isinf(normalLength)&&!isnan(normalLength)?localNormal/normalLength:normal.xyz;
        #endif
            vec4 p=world*localPosition;
            gl_Position=vp*p;
            gl_Position.z=2.0*gl_Position.z-gl_Position.w;
            vec3 n=normalize((normals*vec4(localNormal,0)).xyz);
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
}
