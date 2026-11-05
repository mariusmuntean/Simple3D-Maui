using System.Numerics;

namespace Simple3D.Shared.Mirror;

/// <summary>2D face landmarks in any frame with equal X and Y scale and Y up. "Left" and "right" are image sides, not the person's.</summary>
/// <param name="LeftEye">Outline of the eye on the image's left (the person's right eye).</param>
/// <param name="RightEye">Outline of the eye on the image's right.</param>
/// <param name="LeftBrow">Eyebrow above <paramref name="LeftEye"/>.</param>
/// <param name="RightBrow">Eyebrow above <paramref name="RightEye"/>.</param>
/// <param name="LeftMouthCorner">Mouth corner on the image's left.</param>
/// <param name="RightMouthCorner">Mouth corner on the image's right.</param>
/// <param name="UpperInnerLip">Central points of the upper lip's inner (lower) edge.</param>
/// <param name="LowerInnerLip">Central points of the lower lip's inner (upper) edge.</param>
public sealed record FaceLandmarks(Vector2[] LeftEye, Vector2[] RightEye, Vector2[] LeftBrow, Vector2[] RightBrow,
    Vector2 LeftMouthCorner, Vector2 RightMouthCorner, Vector2[] UpperInnerLip, Vector2[] LowerInnerLip);

/// <summary>Expression measurements from <see cref="FaceLandmarks"/>, shared by the Apple Vision and ML Kit detectors.
/// <c>scripts/extract-pose-landmarks.swift</c> repeats the same math for host fixtures; keep them in step.</summary>
/// <remarks>Distances are taken along the face's own axes (X from eye centre to eye centre, which removes roll) and divided by the
/// distance between the eye centres, so they do not depend on image or face size. Vertical distances divide by that distance over
/// cos(yaw), because turning the head shortens the eye distance but not the face's height. The constants come from Apple Vision
/// on a dozen stock clips of eight people: they map typical neutral faces to about 0 and clear expressions to 0.6–1.</remarks>
public static class FaceGeometry
{
    // Inner-lip gap: up to 0.05 eye distances while closed or speaking softly, 0.2 in speech, 0.4–0.5 for a wide "O".
    const float MouthClosed = .05f, MouthWide = .40f;
    // Mouth width (corner to corner over eye distance): 0.62–0.74 neutral, 0.78 talking, 0.9–1.2 smiling or laughing.
    const float SmileNeutral = .75f, SmileBroad = 1f;
    // Brow centre above the eye corners: 0.20–0.30 eye distances neutral (it varies by person; bangs hide it), 0.35+ raised, less when frowning.
    // Eye corners, unlike the eye centre, stay put while the eyelids move, so blinks and wide eyes do not move the brows.
    const float BrowNeutral = .26f, BrowRange = .10f;
    // Eye opening (height over width): 0.28–0.45 open, 0.20 squinting in a laugh, 0.06–0.13 closed.
    const float EyeOpen = .28f, EyeClosed = .12f;

    /// <summary>The measured face. <paramref name="smile"/> and the per-eye blinks replace the geometric estimates when the detector
    /// classifies them itself (ML Kit); the blinks are for the person's own eyes.</summary>
    public static FaceFrame Measure(FaceLandmarks face, float yaw, float pitch, float roll,
        float? smile = null, float? blinkLeft = null, float? blinkRight = null)
    {
        ArgumentNullException.ThrowIfNull(face);
        Vector2[][] regions = [face.LeftEye, face.RightEye, face.LeftBrow, face.RightBrow, face.UpperInnerLip, face.LowerInnerLip];
        if (regions.Any(region => region.Length == 0)) return new(yaw, pitch, roll, 0, smile ?? 0, 0, blinkLeft ?? 0, blinkRight ?? 0);
        Vector2 leftEye = Mean(face.LeftEye), rightEye = Mean(face.RightEye);
        var eyeDistance = Vector2.Distance(leftEye, rightEye);
        if (eyeDistance <= 0 || !float.IsFinite(eyeDistance)) return new(yaw, pitch, roll, 0, smile ?? 0, 0, blinkLeft ?? 0, blinkRight ?? 0);
        var x = (rightEye - leftEye) / eyeDistance;
        var y = new Vector2(-x.Y, x.X);
        var height = eyeDistance / MathF.Max(MathF.Cos(yaw), .5f);
        var gap = Vector2.Dot(Mean(face.UpperInnerLip) - Mean(face.LowerInnerLip), y) / height;
        var width = MathF.Abs(Vector2.Dot(face.RightMouthCorner - face.LeftMouthCorner, x)) / eyeDistance;
        var brows = (Brow(face.LeftBrow, face.LeftEye, x, y) + Brow(face.RightBrow, face.RightEye, x, y)) / 2 / height;
        return new(yaw, pitch, roll,
            Unit((gap - MouthClosed) / (MouthWide - MouthClosed)),
            smile ?? Unit((width - SmileNeutral) / (SmileBroad - SmileNeutral)),
            Math.Clamp((brows - BrowNeutral) / BrowRange, -1, 1),
            // The person's left eye is on the image's right.
            blinkLeft ?? Blink(face.RightEye, x, y), blinkRight ?? Blink(face.LeftEye, x, y));
    }

    static float Brow(Vector2[] brow, Vector2[] eye, Vector2 x, Vector2 y)
    {
        Vector2 inner = eye[0], outer = eye[0];
        foreach (var p in eye)
        {
            if (Vector2.Dot(p, x) < Vector2.Dot(inner, x)) inner = p;
            if (Vector2.Dot(p, x) > Vector2.Dot(outer, x)) outer = p;
        }
        return Vector2.Dot(Mean(brow) - (inner + outer) / 2, y);
    }

    static float Blink(Vector2[] eye, Vector2 x, Vector2 y)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in eye)
        {
            float u = Vector2.Dot(p, x), v = Vector2.Dot(p, y);
            minX = MathF.Min(minX, u); maxX = MathF.Max(maxX, u);
            minY = MathF.Min(minY, v); maxY = MathF.Max(maxY, v);
        }
        return maxX > minX ? Unit((EyeOpen - (maxY - minY) / (maxX - minX)) / (EyeOpen - EyeClosed)) : 0;
    }

    static Vector2 Mean(Vector2[] points)
    {
        var sum = Vector2.Zero;
        foreach (var p in points) sum += p;
        return points.Length == 0 ? sum : sum / points.Length;
    }

    static float Unit(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
