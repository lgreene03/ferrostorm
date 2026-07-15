using Godot;

namespace Ferrostorm.Client;

/// <summary>Classic RTS rig: 50-degree three-quarter view, WASD/edge pan,
/// wheel zoom toward the cursor. Attach to a Camera3D. Wave 3 (doc 20):
/// panning drives a target the camera chases with exponential smoothing,
/// clamped to the map bounds; FlyTo glides to minimap jumps; AddTrauma feeds
/// a decaying shake applied through H/VOffset so it never fights the pan or
/// zoom smoothing state.</summary>
public partial class RtsCamera : Camera3D
{
    [Export] public float PanSpeed = 24f;
    [Export] public float ZoomStep = 2.4f;
    [Export] public float MinHeight = 8f, MaxHeight = 42f;
    [Export] public float Damping = 12f;
    public Vector2 BoundsMin = Vector2.Zero;
    public Vector2 BoundsMax = new(64, 64);

    // W3-18: graded edge-pan band width and the keyboard/edge speed ramp.
    private const float EdgeBand = 24f;

    private CameraAttributesPractical _attrs = null!;
    private Vector3 _target;
    private float _trauma;
    private float _panRamp;

    public override void _Ready()
    {
        RotationDegrees = new Vector3(-50, 0, 0);
        _target = Position;
        // W1-10: tilt-shift far DOF, strongest zoomed in; auto-exposure
        // explicitly off so muzzle flashes never pump the frame.
        _attrs = new CameraAttributesPractical
        {
            DofBlurFarEnabled = true,
            DofBlurFarDistance = 55f,
            DofBlurFarTransition = 25f,
            DofBlurAmount = 0.05f,
            AutoExposureEnabled = false,
        };
        Attributes = _attrs;
    }

    /// <summary>Glide the view to a ground point (minimap jumps).</summary>
    public void FlyTo(Vector3 ground)
        => _target = new Vector3(ground.X, _target.Y, ground.Z + _target.Y * 0.55f);

    /// <summary>Teleport with no glide (initial placement).</summary>
    public void Snap(Vector3 pos)
    {
        Position = pos;
        _target = pos;
    }

    /// <summary>W3-13: pooled screen-shake trauma, clamped to 1.</summary>
    public void AddTrauma(float amount) => _trauma = Mathf.Min(1f, _trauma + amount);

    public override void _Process(double delta)
    {
        var move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Z -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Z += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
        var mouse = GetViewport().GetMousePosition();
        var size = GetViewport().GetVisibleRect().Size;
        // W3-18: graded 24px edge band replaces the binary 6px tests. The
        // quadratic curve is gentle at the band's inner edge, full speed at
        // the screen border; Clamp handles the cursor leaving the window.
        float wL = Mathf.Clamp((EdgeBand - mouse.X) / EdgeBand, 0f, 1f);
        float wR = Mathf.Clamp((mouse.X - (size.X - EdgeBand)) / EdgeBand, 0f, 1f);
        float wU = Mathf.Clamp((EdgeBand - mouse.Y) / EdgeBand, 0f, 1f);
        float wD = Mathf.Clamp((mouse.Y - (size.Y - EdgeBand)) / EdgeBand, 0f, 1f);
        move.X += wR * wR - wL * wL;
        move.Z += wD * wD - wU * wU;
        // W3-18: pan speed ramps up over 0.35s from a 40 percent start so
        // taps nudge and holds accelerate. LimitLength (not Normalized)
        // preserves the analogue edge weights, normalising only diagonals.
        _panRamp = move != Vector3.Zero ? Mathf.Min(1f, _panRamp + (float)delta / 0.35f) : 0f;
        float speed = PanSpeed * (0.4f + 0.6f * _panRamp);
        // W3-11: pan the target, clamp it to the map, chase it exponentially.
        _target += move.LimitLength(1f) * speed * (float)delta * (_target.Y / 16f);
        _target.X = Mathf.Clamp(_target.X, BoundsMin.X, BoundsMax.X);
        // The 0.55f factor is the look-at offset shared with Minimap.Refresh.
        float zOff = _target.Y * 0.55f;
        _target.Z = Mathf.Clamp(_target.Z, BoundsMin.Y + zOff, BoundsMax.Y + zOff);
        Position = Position.Lerp(_target, 1f - Mathf.Exp(-Damping * (float)delta));
        _attrs.DofBlurFarDistance = Position.Y * 2.2f + 12f;

        // W3-13: trauma decays linearly; shake is trauma squared, applied as
        // layered sines through H/VOffset (never fights the smoothing state).
        // Time.GetTicksMsec is presentation-side only; the determinism law
        // binds /sim (BattlefieldView header).
        _trauma = Mathf.Max(0f, _trauma - 1.1f * (float)delta);
        float shake = _trauma * _trauma;
        if (shake > 0.0005f)
        {
            float t = Time.GetTicksMsec() / 1000f;
            HOffset = shake * 0.35f * (Mathf.Sin(t * 37.1f) + 0.5f * Mathf.Sin(t * 23.7f));
            VOffset = shake * 0.35f * (Mathf.Sin(t * 41.3f + 1.7f) + 0.5f * Mathf.Sin(t * 19.3f));
        }
        else
        {
            HOffset = 0f;
            VOffset = 0f;
        }
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        // W3-12: zoom along the cursor ray. Moving distance k along the ray
        // changes height by exactly k*d.Y, so solving k for the clamped
        // target height gives zoom-in that slides the camera toward the point
        // under the cursor and zoom-out that retreats along the same ray;
        // MinHeight/MaxHeight are enforced exactly with no separate clamp.
        // The pitch is fixed at -50 degrees and the mouse is always over
        // ground when wheeling in play, so d.Y is always negative in
        // practice; the 0.001 guard covers the degenerate case.
        if (ev is InputEventMouseButton mb && mb.Pressed
            && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown))
        {
            var m = GetViewport().GetMousePosition();
            var d = ProjectRayNormal(m);
            if (Mathf.Abs(d.Y) < 0.001f) return;
            float step = mb.ButtonIndex == MouseButton.WheelUp ? -ZoomStep : ZoomStep;
            float newY = Mathf.Clamp(_target.Y + step, MinHeight, MaxHeight);
            float k = (newY - _target.Y) / d.Y;
            _target += d * k;
        }
    }
}
