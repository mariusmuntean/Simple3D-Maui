#if (DEBUG || NATIVE_RENDER_PROBE) && (IOS || MACCATALYST || ANDROID)
using Simple3D.Core;
using Simple3D.Maui;
#if IOS || MACCATALYST
using Metal;
#else
using Android.Opengl;
#endif
namespace Simple3D.Demo;

internal static class NativeGpuProbe
{
    internal static unsafe void RunIfRequested()
    {
        if(Environment.GetEnvironmentVariable("SIMPLE3D_GPU_PROBE")!="1")return;
        const int size=NativeGpuValidation.Size;
        var prepared=new GpuScene();
#if IOS || MACCATALYST
        var device=MTLDevice.SystemDefault??throw new PlatformNotSupportedException("Metal device unavailable; GPU probe cannot pass.");
        using var renderer=new MetalRenderer(device);
        using var descriptor=MTLTextureDescriptor.CreateTexture2DDescriptor(MTLPixelFormat.BGRA8Unorm,size,size,false);
        // Private like a drawable; read back through a blit into a shared buffer. Shared-storage
        // render targets are not reliably CPU-readable on every device (the iOS simulator returns zeros).
        descriptor.StorageMode=MTLStorageMode.Private;descriptor.Usage=MTLTextureUsage.RenderTarget;
        using var target=device.CreateTexture(descriptor)??throw new InvalidOperationException("Cannot allocate probe target.");
        using var queue=device.CreateCommandQueue()??throw new InvalidOperationException("Cannot create probe queue.");
        using var readback=device.CreateBuffer((nuint)(size*size*4),MTLResourceOptions.StorageModeShared)??throw new InvalidOperationException("Cannot allocate probe readback.");
        Console.WriteLine("GPU_PROBE Metal depth storage: "+(renderer.MemorylessDepth?"memoryless":"private"));
        NativeGpuValidation.Run(renderer.Name, (scene,camera,options)=>
        {
            prepared.Update(scene,camera,size,size);
            renderer.ForceComposite=options.ForceComposite;
            renderer.Render(new(prepared,scene,camera,options.Selected,NativeGpuValidation.Background,options.OutlineRadius),size,size,target,waitForCompletion:true);
            if(options.SkipReadback)return Array.Empty<uint>();
            using(var command=queue.CommandBuffer()??throw new InvalidOperationException("Probe command buffer unavailable."))
            {
                using(var blit=command.BlitCommandEncoder??throw new InvalidOperationException("Probe blit unavailable."))
                {
                    blit.CopyFromTexture(target,0,0,new MTLOrigin(0,0,0),new MTLSize(size,size,1),readback,0,(nuint)(size*4),(nuint)(size*size*4));
                    blit.EndEncoding();
                }
                command.Commit();command.WaitUntilCompleted();
            }
            var pixels=new uint[size*size];
            new ReadOnlySpan<uint>((void*)readback.Contents,size*size).CopyTo(pixels);
            return pixels;
        },()=>renderer.LastFrameDirect);
#else
        var display=EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
        if(display is null||!EGL14.EglInitialize(display,new int[2],0,new int[2],0))throw new InvalidOperationException("Probe EGL initialization failed.");
        // The view's window config, as a pbuffer: same format and depth buffer.
        var config=AndroidGpuView.ChooseConfig(display,EGL14.EglPbufferBit,depth:true)
            ??throw new InvalidOperationException("Probe requires an ES3 pbuffer with depth (as the view's window surface requests).");
        var context=EGL14.EglCreateContext(display,config,EGL14.EglNoContext,new[]{EGL14.EglContextClientVersion,3,EGL14.EglNone},0);
        var surface=EGL14.EglCreatePbufferSurface(display,config,new[]{EGL14.EglWidth,size,EGL14.EglHeight,size,EGL14.EglNone},0);
        try
        {
            if(!EGL14.EglMakeCurrent(display,surface,surface,context))throw new InvalidOperationException("Probe EGL make-current failed.");
            using var renderer=new GlesRenderer();
            NativeGpuValidation.Run(renderer.Name,(scene,camera,options)=>
            {
                prepared.Update(scene,camera,size,size);
                renderer.ForceComposite=options.ForceComposite;
                renderer.Render(new(prepared,scene,camera,options.Selected,NativeGpuValidation.Background,options.OutlineRadius),size,size,size,size);
                if(options.SkipReadback){Gles.glFinish();return Array.Empty<uint>();}
                return renderer.ReadPixels(size,size);
            },()=>renderer.LastFrameDirect);
        }
        finally
        {
            EGL14.EglMakeCurrent(display,EGL14.EglNoSurface,EGL14.EglNoSurface,EGL14.EglNoContext);
            EGL14.EglDestroySurface(display,surface);EGL14.EglDestroyContext(display,context);
        }
#endif
        Environment.Exit(0);
    }
}
#endif
