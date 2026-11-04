#if IOS || MACCATALYST
using System.Diagnostics;
using CoreAnimation;
using Foundation;
using Metal;
using Simple3D.Core;
namespace Simple3D.Maui;

internal sealed unsafe class MetalRenderer : IDisposable
{
    private readonly IMTLDevice _device;
    private IMTLCommandQueue? _queue;
    private IMTLRenderPipelineState? _pipeline,_screen;
    private IMTLDepthStencilState? _depthState;
    private IMTLTexture? _color,_mask,_depth;
    private int _width,_height;
    private readonly Dictionary<GpuGeometry,IMTLBuffer> _meshes=new();
    private readonly Dictionary<Texture2D,(IMTLTexture Image,IMTLSamplerState Sampler)> _textures=new();
    private static readonly Texture2D White=new(1,1,new[]{0xFFFFFFFFu});
    internal MetalRenderer(IMTLDevice device)
    {
        _device=device;
        try
        {
            _queue=device.CreateCommandQueue(3)??throw new InvalidOperationException("Cannot create Metal command queue.");
            using var options=new MTLCompileOptions();
            using var library=device.CreateLibrary(GpuShaders.Metal,options,out var error)
                ??throw new InvalidOperationException("Metal shader compilation: "+error?.LocalizedDescription);
            _pipeline=Pipeline(library,"sceneVertex","sceneFragment",true);
            _screen=Pipeline(library,"screenVertex","screenFragment",false);
            using var depth=new MTLDepthStencilDescriptor { DepthCompareFunction=MTLCompareFunction.Less,DepthWriteEnabled=true };
            _depthState=device.CreateDepthStencilState(depth)??throw new InvalidOperationException("Cannot create Metal depth state.");
        }
        catch { Dispose();throw; }
    }
    private IMTLRenderPipelineState Pipeline(IMTLLibrary library,string vertex,string fragment,bool scene)
    {
        using var v=library.CreateFunction(vertex);using var f=library.CreateFunction(fragment);
        using var descriptor=new MTLRenderPipelineDescriptor { VertexFunction=v,FragmentFunction=f };
        descriptor.ColorAttachments[0].PixelFormat=MTLPixelFormat.BGRA8Unorm;
        if(scene) { descriptor.ColorAttachments[1].PixelFormat=MTLPixelFormat.RGBA8Unorm;descriptor.DepthAttachmentPixelFormat=MTLPixelFormat.Depth32Float; }
        return _device.CreateRenderPipelineState(descriptor,out var error)??throw new InvalidOperationException("Metal pipeline: "+error?.LocalizedDescription);
    }
    internal double Render(GpuFrame frame,int width,int height,IMTLTexture output,ICAMetalDrawable? drawable=null,bool waitForCompletion=false,Action<Exception>? onError=null)
    {
        var start=Stopwatch.GetTimestamp();
        Resize(width,height);
        var usedMeshes=frame.Geometry.Items.Select(i=>i.Geometry).ToHashSet();
        var usedTextures=frame.Geometry.Items.Select(i=>i.Shape.Material.Texture??White).ToHashSet();
        var bytes=usedMeshes.Sum(g=>(long)g.Vertices.Length*4)+usedTextures.Sum(t=>(long)t.Width*t.Height*4);
        if(bytes>256L*1024*1024)throw new InvalidOperationException("GPU scene resources exceed 256 MiB.");
        foreach(var key in _meshes.Keys.Where(k=>!usedMeshes.Contains(k)).ToArray()) { _meshes[key].Dispose();_meshes.Remove(key); }
        foreach(var key in _textures.Keys.Where(k=>!usedTextures.Contains(k)).ToArray()) { var t=_textures[key];t.Image.Dispose();t.Sampler.Dispose();_textures.Remove(key); }
        using var command=_queue!.CommandBuffer()??throw new InvalidOperationException("Metal command buffer unavailable.");
        using var pass=new MTLRenderPassDescriptor();
        var bg=GpuGeometry.Rgba(frame.Background);
        Attachment(pass.ColorAttachments[0],_color!,new MTLClearColor(bg.X,bg.Y,bg.Z,1));
        Attachment(pass.ColorAttachments[1],_mask!,new MTLClearColor(0,0,0,0));
        pass.DepthAttachment!.Texture=_depth;pass.DepthAttachment.LoadAction=MTLLoadAction.Clear;
        pass.DepthAttachment.StoreAction=MTLStoreAction.DontCare;pass.DepthAttachment.ClearDepth=1;
        using(var encoder=command.CreateRenderCommandEncoder(pass)??throw new InvalidOperationException("Metal encoder unavailable."))
        {
            encoder.SetRenderPipelineState(_pipeline!);encoder.SetDepthStencilState(_depthState!);
            encoder.SetFrontFacingWinding(MTLWinding.CounterClockwise);
            foreach(var item in frame.Geometry.Items)
            {
                if(!_meshes.TryGetValue(item.Geometry,out var buffer))
                {
                    fixed(float* p=item.Geometry.Vertices) buffer=_device.CreateBuffer((nint)p,(nuint)(item.Geometry.Vertices.Length*4),MTLResourceOptions.StorageModeShared)
                        ??throw new InvalidOperationException("Metal vertex allocation failed.");
                    _meshes.Add(item.Geometry,buffer);
                }
                encoder.SetVertexBuffer(buffer,0,0);
                var u=GpuUniforms.Create(frame,item);
                // Metal copies these small per-draw constants into command-owned storage.
                encoder.SetVertexBytes((nint)(&u),(nuint)sizeof(GpuUniforms),1);
                encoder.SetFragmentBytes((nint)(&u),(nuint)sizeof(GpuUniforms),1);
                var texture=Texture(item.Shape.Material.Texture??White);
                encoder.SetFragmentTexture(texture.Image,0);encoder.SetFragmentSamplerState(texture.Sampler,0);
                encoder.SetCullMode(item.Shape.Material.DoubleSided?MTLCullMode.None:MTLCullMode.Back);
                encoder.DrawPrimitives(MTLPrimitiveType.Triangle,0,(nuint)(item.Geometry.Vertices.Length/GpuGeometry.FloatsPerVertex));
            }
            encoder.EndEncoding();
        }
        using var screenPass=new MTLRenderPassDescriptor();
        Attachment(screenPass.ColorAttachments[0],output,new MTLClearColor(0,0,0,1));
        using(var encoder=command.CreateRenderCommandEncoder(screenPass)??throw new InvalidOperationException("Metal presentation encoder unavailable."))
        {
            encoder.SetRenderPipelineState(_screen!);encoder.SetFragmentTexture(_color!,0);encoder.SetFragmentTexture(_mask!,1);
            encoder.DrawPrimitives(MTLPrimitiveType.Triangle,0,3);encoder.EndEncoding();
        }
        if(onError is not null) command.AddCompletedHandler(completed =>
        {
            if(completed.Status == MTLCommandBufferStatus.Error)
                onError(new InvalidOperationException("Metal command failed: " + completed.Error?.LocalizedDescription));
        });
        if(drawable is not null)command.PresentDrawable(drawable);
        command.Commit();
        if(waitForCompletion)
        {
            command.WaitUntilCompleted();
            if(command.Status==MTLCommandBufferStatus.Error)throw new InvalidOperationException("Metal command failed: "+command.Error?.LocalizedDescription);
        }
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    private static void Attachment(MTLRenderPassColorAttachmentDescriptor attachment,IMTLTexture texture,MTLClearColor clear)
    { attachment.Texture=texture;attachment.LoadAction=MTLLoadAction.Clear;attachment.StoreAction=MTLStoreAction.Store;attachment.ClearColor=clear; }
    private (IMTLTexture Image,IMTLSamplerState Sampler) Texture(Texture2D texture)
    {
        if(_textures.TryGetValue(texture,out var cached))return cached;
        using var descriptor=MTLTextureDescriptor.CreateTexture2DDescriptor(MTLPixelFormat.RGBA8Unorm,(nuint)texture.Width,(nuint)texture.Height,false);
        descriptor.StorageMode=MTLStorageMode.Shared;descriptor.Usage=MTLTextureUsage.ShaderRead;
        var image=_device.CreateTexture(descriptor)??throw new InvalidOperationException("Metal texture allocation failed.");
        IMTLSamplerState? sampler=null;
        try
        {
            var pixels=new byte[texture.Width*texture.Height*4];
            for(var i=0;i<texture.Pixels.Count;i++) { var c=texture.Pixels[i];pixels[4*i]=(byte)(c>>16);pixels[4*i+1]=(byte)(c>>8);pixels[4*i+2]=(byte)c;pixels[4*i+3]=255; }
            fixed(byte* p=pixels)image.ReplaceRegion(MTLRegion.Create2D(0,0,(nuint)texture.Width,(nuint)texture.Height),0,(nint)p,(nuint)(texture.Width*4));
            var filter=texture.Filter==TextureFilter.Nearest?MTLSamplerMinMagFilter.Nearest:MTLSamplerMinMagFilter.Linear;
            var address=texture.AddressMode==TextureAddressMode.Repeat?MTLSamplerAddressMode.Repeat:MTLSamplerAddressMode.ClampToEdge;
            using var sampling=new MTLSamplerDescriptor { MinFilter=filter,MagFilter=filter,SAddressMode=address,TAddressMode=address };
            sampler=_device.CreateSamplerState(sampling)??throw new InvalidOperationException("Metal sampler unavailable.");
            cached=(image,sampler);_textures.Add(texture,cached);return cached;
        }
        catch { image.Dispose();sampler?.Dispose();throw; }
    }
    private void Resize(int width,int height)
    {
        if(_width==width&&_height==height)return;
        ReleaseTargets();
        _color=Target(MTLPixelFormat.BGRA8Unorm,width,height);
        _mask=Target(MTLPixelFormat.RGBA8Unorm,width,height);
        _depth=Target(MTLPixelFormat.Depth32Float,width,height);
        _width=width;_height=height;
    }
    private IMTLTexture Target(MTLPixelFormat format,int width,int height)
    {
        using var descriptor=MTLTextureDescriptor.CreateTexture2DDescriptor(format,(nuint)width,(nuint)height,false);
        descriptor.StorageMode=MTLStorageMode.Private;descriptor.Usage=MTLTextureUsage.RenderTarget|MTLTextureUsage.ShaderRead;
        return _device.CreateTexture(descriptor)??throw new InvalidOperationException("Metal render target allocation failed.");
    }
    private void ReleaseTargets() { _color?.Dispose();_mask?.Dispose();_depth?.Dispose();_color=_mask=_depth=null;_width=_height=0; }
    public void Dispose()
    {
        foreach(var b in _meshes.Values)b.Dispose();_meshes.Clear();
        foreach(var t in _textures.Values) { t.Image.Dispose();t.Sampler.Dispose(); }_textures.Clear();
        ReleaseTargets();_depthState?.Dispose();_pipeline?.Dispose();_screen?.Dispose();_queue?.Dispose();
        _depthState=null;_pipeline=_screen=null;_queue=null;
    }
}
#endif
