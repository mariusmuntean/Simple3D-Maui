#if IOS || MACCATALYST
using CoreAnimation;
using Foundation;
using Metal;
using ObjCRuntime;
using Simple3D.Core;
namespace Simple3D.Maui;

internal sealed unsafe class MetalRenderer : IDisposable
{
    private const MTLPixelFormat OutputFormat = MTLPixelFormat.BGRA8Unorm;
    private readonly IMTLDevice _device;
    private IMTLCommandQueue? _queue;
    private IMTLRenderPipelineState? _scenePipeline, _directPipeline, _screenPipeline;
    private IMTLDepthStencilState? _depthState;
    private IMTLTexture? _color, _mask, _depth;
    private int _targetWidth, _targetHeight, _depthWidth, _depthHeight;
    private readonly GpuResources<IMTLBuffer, (IMTLTexture Image, IMTLSamplerState Sampler)> _resources = new(
        buffer => buffer.Dispose(), texture => { texture.Image.Dispose(); texture.Sampler.Dispose(); });
    // Created once: reports failed command buffers (MTLCommandBufferError code, description).
    private readonly Action<IMTLCommandBuffer>? _completed;
    internal string Name { get; }
    internal int GeometryUploads { get; private set; }
    internal int TextureUploads { get; private set; }
    /// <summary>Always use the offscreen mask/outline composite. For validation that both paths match.</summary>
    internal bool ForceComposite { get; set; }
    /// <summary>Whether the last frame rendered straight to the output (no offscreen targets or fullscreen pass).</summary>
    internal bool LastFrameDirect { get; private set; }
    /// <summary>Depth lives only in tile memory (Apple GPUs on device); otherwise a private texture.</summary>
    internal bool MemorylessDepth { get; }
    internal MetalRenderer(IMTLDevice device, Action<nint, string>? onError = null)
    {
        _device = device;
        Name = "Metal / " + device.Name;
        if (onError is not null)
            _completed = completed =>
            {
                if (completed.Status == MTLCommandBufferStatus.Error)
                    onError(completed.Error?.Code ?? 0, "Metal command failed: " + completed.Error?.LocalizedDescription);
            };
        try
        {
            _queue = device.CreateCommandQueue(3) ?? throw new InvalidOperationException("Cannot create Metal command queue.");
            using var options = new MTLCompileOptions();
            using var library = device.CreateLibrary(GpuShaders.Metal, options, out var error)
                ?? throw new InvalidOperationException("Metal shader compilation: " + error?.LocalizedDescription);
            _scenePipeline = Pipeline(library, "sceneVertex", "sceneFragment", mask: true, depth: true);
            _directPipeline = Pipeline(library, "sceneVertex", "sceneFragmentDirect", mask: false, depth: true);
            _screenPipeline = Pipeline(library, "screenVertex", "screenFragment", mask: false, depth: false);
            // Reversed Z (near = 1, far = 0) with a float depth buffer keeps precision roughly
            // proportional to distance instead of collapsing toward 1.
            using var depth = new MTLDepthStencilDescriptor { DepthCompareFunction = MTLCompareFunction.Greater, DepthWriteEnabled = true };
            _depthState = device.CreateDepthStencilState(depth) ?? throw new InvalidOperationException("Cannot create Metal depth state.");
            // Memoryless needs an Apple-family GPU; the iOS simulator does not provide it.
#if IOS
            MemorylessDepth = Runtime.Arch == Arch.DEVICE && device.SupportsFamily(MTLGpuFamily.Apple2);
#else
            MemorylessDepth = device.SupportsFamily(MTLGpuFamily.Apple2);
#endif
        }
        catch { Dispose(); throw; }
    }
    private IMTLRenderPipelineState Pipeline(IMTLLibrary library, string vertex, string fragment, bool mask, bool depth)
    {
        using var v = library.CreateFunction(vertex); using var f = library.CreateFunction(fragment);
        using var descriptor = new MTLRenderPipelineDescriptor { VertexFunction = v, FragmentFunction = f };
        descriptor.ColorAttachments[0].PixelFormat = OutputFormat;
        if (mask) descriptor.ColorAttachments[1].PixelFormat = MTLPixelFormat.R8Unorm;
        if (depth) descriptor.DepthAttachmentPixelFormat = MTLPixelFormat.Depth32Float;
        return _device.CreateRenderPipelineState(descriptor, out var error) ?? throw new InvalidOperationException("Metal pipeline: " + error?.LocalizedDescription);
    }
    // presentWithTransaction: Present inside the current Core Animation transaction (requires the layer's presentsWithTransaction) so UIKit content drawn in the same pass appears in the same frame.
    internal void Render(GpuFrame frame, int width, int height, IMTLTexture output, ICAMetalDrawable? drawable = null,
        bool waitForCompletion = false, bool presentWithTransaction = false)
    {
        GeometryUploads = TextureUploads = 0;
        _resources.Prepare(frame);
        var direct = !ForceComposite && frame.CanRenderDirect(width, height, (int)output.Width, (int)output.Height);
        LastFrameDirect = direct;
        EnsureDepth(width, height);
        if (!direct) EnsureTargets(width, height);
        using var command = _queue!.CommandBuffer() ?? throw new InvalidOperationException("Metal command buffer unavailable.");
        var bg = GpuGeometry.Rgba(frame.Background);
        using (var pass = new MTLRenderPassDescriptor())
        {
            Attachment(pass.ColorAttachments[0], direct ? output : _color!, MTLLoadAction.Clear, new MTLClearColor(bg.X, bg.Y, bg.Z, 1));
            if (!direct) Attachment(pass.ColorAttachments[1], _mask!, MTLLoadAction.Clear, new MTLClearColor(0, 0, 0, 0));
            pass.DepthAttachment!.Texture = _depth; pass.DepthAttachment.LoadAction = MTLLoadAction.Clear;
            pass.DepthAttachment.StoreAction = MTLStoreAction.DontCare; pass.DepthAttachment.ClearDepth = 0;
            using var encoder = command.CreateRenderCommandEncoder(pass) ?? throw new InvalidOperationException("Metal encoder unavailable.");
            encoder.SetRenderPipelineState(direct ? _directPipeline! : _scenePipeline!); encoder.SetDepthStencilState(_depthState!);
            encoder.SetFrontFacingWinding(MTLWinding.CounterClockwise);
            var viewProjection = frame.Geometry.ReversedViewProjection;
            IMTLTexture? boundTexture = null;
            MTLCullMode? cullState = null;
            var items = frame.Geometry.Items;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                encoder.SetVertexBuffer(Mesh(item.Geometry), 0, 0);
                // Metal copies these small per-draw constants into command-owned storage.
                var u = GpuUniforms.Create(frame, item, viewProjection);
                encoder.SetVertexBytes((nint)(&u), (nuint)sizeof(GpuUniforms), 1);
                var texture = Texture(GpuResources.TextureOf(item));
                if (!ReferenceEquals(texture.Image, boundTexture))
                {
                    encoder.SetFragmentTexture(texture.Image, 0); encoder.SetFragmentSamplerState(texture.Sampler, 0);
                    boundTexture = texture.Image;
                }
                var cull = item.Shape.Material.DoubleSided ? MTLCullMode.None : MTLCullMode.Back;
                if (cull != cullState) { encoder.SetCullMode(cull); cullState = cull; }
                encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, (nuint)item.Geometry.VertexCount);
            }
            encoder.EndEncoding();
        }
        if (!direct)
        {
            using var screenPass = new MTLRenderPassDescriptor();
            // The fullscreen triangle overwrites every pixel: nothing to load.
            Attachment(screenPass.ColorAttachments[0], output, MTLLoadAction.DontCare, default);
            using var encoder = command.CreateRenderCommandEncoder(screenPass) ?? throw new InvalidOperationException("Metal presentation encoder unavailable.");
            encoder.SetRenderPipelineState(_screenPipeline!); encoder.SetFragmentTexture(_color!, 0); encoder.SetFragmentTexture(_mask!, 1);
            var radius = frame.OutlineRadius;
            encoder.SetFragmentBytes((nint)(&radius), sizeof(int), 0);
            encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3); encoder.EndEncoding();
        }
        if (_completed is not null) command.AddCompletedHandler(_completed);
        if (drawable is not null && presentWithTransaction)
        {
            command.Commit();
            command.WaitUntilScheduled();
            drawable.Present();
        }
        else
        {
            if (drawable is not null) command.PresentDrawable(drawable);
            command.Commit();
        }
        if (waitForCompletion)
        {
            command.WaitUntilCompleted();
            if (command.Status == MTLCommandBufferStatus.Error) throw new InvalidOperationException("Metal command failed: " + command.Error?.LocalizedDescription);
        }
    }
    private static void Attachment(MTLRenderPassColorAttachmentDescriptor attachment, IMTLTexture texture, MTLLoadAction load, MTLClearColor clear)
    { attachment.Texture = texture; attachment.LoadAction = load; attachment.StoreAction = MTLStoreAction.Store; attachment.ClearColor = clear; }
    private IMTLBuffer Mesh(GpuGeometry geometry)
    {
        if (_resources.Meshes.TryGetValue(geometry, out var buffer)) return buffer;
        var vertices = geometry.Expand();
        fixed (float* p = vertices) buffer = _device.CreateBuffer((nint)p, (nuint)(vertices.Length * sizeof(float)), MTLResourceOptions.StorageModeShared)
            ?? throw new InvalidOperationException("Metal vertex allocation failed.");
        _resources.Meshes.Add(geometry, buffer);
        GeometryUploads++;
        return buffer;
    }
    private (IMTLTexture Image, IMTLSamplerState Sampler) Texture(Texture2D texture)
    {
        if (_resources.Textures.TryGetValue(texture, out var cached)) return cached;
        using var descriptor = MTLTextureDescriptor.CreateTexture2DDescriptor(MTLPixelFormat.RGBA8Unorm, (nuint)texture.Width, (nuint)texture.Height, false);
        descriptor.StorageMode = MTLStorageMode.Shared; descriptor.Usage = MTLTextureUsage.ShaderRead;
        var image = _device.CreateTexture(descriptor) ?? throw new InvalidOperationException("Metal texture allocation failed.");
        IMTLSamplerState? sampler = null;
        try
        {
            var pixels = GpuResources.Rgba8(texture);
            fixed (byte* p = pixels) image.ReplaceRegion(MTLRegion.Create2D(0, 0, (nuint)texture.Width, (nuint)texture.Height), 0, (nint)p, (nuint)(texture.Width * 4));
            var filter = texture.Filter == TextureFilter.Nearest ? MTLSamplerMinMagFilter.Nearest : MTLSamplerMinMagFilter.Linear;
            var address = texture.AddressMode == TextureAddressMode.Repeat ? MTLSamplerAddressMode.Repeat : MTLSamplerAddressMode.ClampToEdge;
            using var sampling = new MTLSamplerDescriptor { MinFilter = filter, MagFilter = filter, SAddressMode = address, TAddressMode = address };
            sampler = _device.CreateSamplerState(sampling) ?? throw new InvalidOperationException("Metal sampler unavailable.");
            cached = (image, sampler); _resources.Textures.Add(texture, cached); TextureUploads++; return cached;
        }
        catch { image.Dispose(); sampler?.Dispose(); throw; }
    }
    private void EnsureDepth(int width, int height)
    {
        if (_depth is not null && _depthWidth == width && _depthHeight == height) return;
        _depth?.Dispose(); _depth = null;
        using var descriptor = MTLTextureDescriptor.CreateTexture2DDescriptor(MTLPixelFormat.Depth32Float, (nuint)width, (nuint)height, false);
        descriptor.StorageMode = MemorylessDepth ? MTLStorageMode.Memoryless : MTLStorageMode.Private;
        descriptor.Usage = MTLTextureUsage.RenderTarget;
        _depth = _device.CreateTexture(descriptor) ?? throw new InvalidOperationException("Metal depth allocation failed.");
        _depthWidth = width; _depthHeight = height;
    }
    // Offscreen color and mask exist only once a selection is outlined (or a capped size is upscaled).
    private void EnsureTargets(int width, int height)
    {
        if (_color is not null && _targetWidth == width && _targetHeight == height) return;
        ReleaseTargets();
        _color = Target(OutputFormat, width, height);
        _mask = Target(MTLPixelFormat.R8Unorm, width, height);
        _targetWidth = width; _targetHeight = height;
    }
    private IMTLTexture Target(MTLPixelFormat format, int width, int height)
    {
        using var descriptor = MTLTextureDescriptor.CreateTexture2DDescriptor(format, (nuint)width, (nuint)height, false);
        descriptor.StorageMode = MTLStorageMode.Private; descriptor.Usage = MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderRead;
        return _device.CreateTexture(descriptor) ?? throw new InvalidOperationException("Metal render target allocation failed.");
    }
    private void ReleaseTargets() { _color?.Dispose(); _mask?.Dispose(); _color = _mask = null; _targetWidth = _targetHeight = 0; }
    public void Dispose()
    {
        _resources.Clear();
        ReleaseTargets(); _depth?.Dispose(); _depth = null;
        _depthState?.Dispose(); _scenePipeline?.Dispose(); _directPipeline?.Dispose(); _screenPipeline?.Dispose(); _queue?.Dispose();
        _depthState = null; _scenePipeline = _directPipeline = _screenPipeline = null; _queue = null;
    }
}
#endif
