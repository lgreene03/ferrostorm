using Godot;

namespace Ferrostorm.Client;

/// <summary>Classic RTS rig: 50-degree three-quarter view, WASD/edge pan,
/// wheel zoom along the view axis. Attach to a Camera3D.</summary>
public partial class RtsCamera : Camera3D
{
    [Export] public float PanSpeed = 24f;
    [Export] public float ZoomStep = 2.4f;
    [Export] public float MinHeight = 8f, MaxHeight = 42f;

    public override void _Ready()
    {
        RotationDegrees = new Vector3(-50, 0, 0);
    }

    public override void _Process(double delta)
    {
        var move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Z -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Z += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
        var mouse = GetViewport().GetMousePosition();
        var size = GetViewport().GetVisibleRect().Size;
        if (mouse.X < 6) move.X -= 1;
        if (mouse.X > size.X - 6) move.X += 1;
        if (mouse.Y < 6) move.Z -= 1;
        if (mouse.Y > size.Y - 6) move.Z += 1;
        Position += move.Normalized() * PanSpeed * (float)delta * (Position.Y / 16f);
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed)
        {
            var fwd = -GlobalTransform.Basis.Z;
            if (mb.ButtonIndex == MouseButton.WheelUp && Position.Y > MinHeight) Position += fwd * ZoomStep;
            if (mb.ButtonIndex == MouseButton.WheelDown && Position.Y < MaxHeight) Position -= fwd * ZoomStep;
        }
    }
}
