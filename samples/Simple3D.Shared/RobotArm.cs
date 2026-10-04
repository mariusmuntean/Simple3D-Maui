using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared;

public static partial class DemoScenes
{
    /// <summary>Builds a two-joint robot arm with independently moving links.</summary>
    public static DemoScene RobotArm()
    {
        var scene = new Scene();
        scene.Add(Shape.Box(0xFF526B9A).Named("Workcell").Scaled(2.8f, .1f, 1.8f).At(0, -1.06f, 0));
        scene.Add(Shape.Cylinder(0xFF7894C8).Named("Base").Scaled(.72f, .28f, .72f).At(-.35f, -.86f, 0));
        var shoulder = Shape.Sphere(0xFFFFBE79).Named("Shoulder").Scaled(.24f);
        var upperLink = Shape.Box(0xFF7894C8).Named("Upper link").Scaled(.32f, 1.08f, .32f).At(0, .54f, 0);
        var elbowBase = Shape.Group(
            Shape.Sphere(0xFFFFBE79).Named("Elbow").Scaled(.22f),
            Shape.Box(0xFF8DA9FF).Named("Forearm").Scaled(.26f, .9f, .26f).At(0, .45f, 0),
            Shape.Box(0xFF76DBC7).Named("Gripper").Scaled(.46f, .14f, .28f).At(0, .96f, 0))
            .At(0, 1.08f, 0);
        Shape ArmAt(float time)
        {
            var shoulderAngle = .24f + .3f * MathF.Sin(time * 1.5f);
            var elbowAngle = -.48f - .42f * MathF.Sin(time * 2.2f);
            return Shape.Group(shoulder, upperLink, elbowBase.Rotated(0, 0, elbowAngle))
                .Named("Armature").At(-.35f, -.65f, 0).Rotated(0, 0, shoulderAngle);
        }
        var arm = ArmAt(0);
        scene.Add(arm);
        scene.AddLabel(new("TWO JOINTS / PICK A LINK", new(-1.3f, 1.65f, 0), 0xFFE4ECFF));
        var camera = new Camera(5, .55f, .25f);
        camera.FitToScene(scene, 4f / 3);
        return new("Robot Arm", "Inspect linked parts as the shoulder and elbow articulate.", scene, camera,
            AnimateNode(scene, arm, ArmAt));
    }

}
