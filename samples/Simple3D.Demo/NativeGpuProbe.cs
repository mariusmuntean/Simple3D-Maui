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
        descriptor.StorageMode=MTLStorageMode.Shared;descriptor.Usage=MTLTextureUsage.RenderTarget;
        using var target=device.CreateTexture(descriptor)??throw new InvalidOperationException("Cannot allocate probe target.");
        NativeGpuValidation.Run("Metal / "+device.Name, (scene,camera,selected)=>
        {
            prepared.Update(scene,camera,size,size);
            renderer.Render(new(prepared,scene,camera,selected,NativeGpuValidation.Background),size,size,target,waitForCompletion:true);
            var pixels=new uint[size*size];
            fixed(uint* p=pixels)target.GetBytes((nint)p,size*4,MTLRegion.Create2D(0,0,size,size),0);
            return pixels;
        });
#else
        var display=EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
        if(display is null||!EGL14.EglInitialize(display,new int[2],0,new int[2],0))throw new InvalidOperationException("Probe EGL initialization failed.");
        var configs=new EGLConfig[1];var count=new int[1];
        if(!EGL14.EglChooseConfig(display,new[]{EGL14.EglRenderableType,0x0040,EGL14.EglSurfaceType,EGL14.EglPbufferBit,
            EGL14.EglRedSize,8,EGL14.EglGreenSize,8,EGL14.EglBlueSize,8,EGL14.EglAlphaSize,8,EGL14.EglNone},0,configs,0,1,count,0)||count[0]==0)
            throw new InvalidOperationException("Probe requires an ES3 pbuffer.");
        var context=EGL14.EglCreateContext(display,configs[0],EGL14.EglNoContext,new[]{EGL14.EglContextClientVersion,3,EGL14.EglNone},0);
        var surface=EGL14.EglCreatePbufferSurface(display,configs[0],new[]{EGL14.EglWidth,size,EGL14.EglHeight,size,EGL14.EglNone},0);
        try
        {
            if(!EGL14.EglMakeCurrent(display,surface,surface,context))throw new InvalidOperationException("Probe EGL make-current failed.");
            using var renderer=new GlesRenderer();
            NativeGpuValidation.Run(renderer.Name,(scene,camera,selected)=>
            {
                prepared.Update(scene,camera,size,size);renderer.Render(new(prepared,scene,camera,selected,NativeGpuValidation.Background),size,size);
                return renderer.ReadPixels();
            });
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
