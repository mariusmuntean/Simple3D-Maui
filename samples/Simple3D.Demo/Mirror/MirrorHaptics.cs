namespace Simple3D.Demo.Mirror;

public static class MirrorHaptics
{
    public static void PersonFound() => Perform(true);
    public static void PersonLost() => Perform(false);

    static void Perform(bool found) => MainThread.BeginInvokeOnMainThread(() =>
    {
#if IOS
        using var generator = new UIKit.UINotificationFeedbackGenerator();
        generator.Prepare();
        generator.NotificationOccurred(found ? UIKit.UINotificationFeedbackType.Success : UIKit.UINotificationFeedbackType.Warning);
#elif ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            Platform.CurrentActivity?.Window?.DecorView?.PerformHapticFeedback(found
                ? Android.Views.HapticFeedbackConstants.Confirm : Android.Views.HapticFeedbackConstants.Reject);
        else if (HapticFeedback.Default.IsSupported)
            HapticFeedback.Default.Perform(found ? HapticFeedbackType.Click : HapticFeedbackType.LongPress);
#endif
        // Catalyst has no notification haptic device.
    });
}
