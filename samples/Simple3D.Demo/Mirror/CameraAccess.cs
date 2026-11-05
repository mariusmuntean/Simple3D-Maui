namespace Simple3D.Demo.Mirror;

public enum CameraAccessStatus { Granted, Denied, Restricted }

public static class CameraAccess
{
    public static async Task<CameraAccessStatus> RequestAsync()
    {
        var status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.Camera>);
        return status switch
        {
            PermissionStatus.Granted => CameraAccessStatus.Granted,
            PermissionStatus.Restricted or PermissionStatus.Disabled => CameraAccessStatus.Restricted,
            _ => CameraAccessStatus.Denied
        };
    }

    public static void OpenSettings() => MainThread.BeginInvokeOnMainThread(AppInfo.ShowSettingsUI);
}
