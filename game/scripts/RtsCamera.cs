using Godot;

namespace Ferrostorm.Client;

/// <summary>Classic RTS rig: 50-degree three-quarter view, arrow/edge pan
/// (TICKET-P5-SET-01: rebindable InputMap actions; the WASD half is gone, see
/// _Process), wheel zoom toward the cursor. Attach to a Camera3D. Wave 3 (doc 20):
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
        // V3-01 (doc 25): the vertical FOV was never set, so Camera3D ran at
        // Godot's 75 default. At a fixed -50 pitch and 75, the far edge of the
        // frame is ~99 m away at the play height of 22 and ~189 m at the max
        // zoom of 42, beyond the depth of any shipped map, so the top ~20 to 35
        // per cent of every frame was empty off-map void (measured in V0/V1, the
        // single biggest compositional flaw in the frame). 50 pulls the horizon
        // down to fill the frame with battlefield and roughly doubles the pixels
        // per world unit at CAM-A (about 18 to about 27), without reading
        // telephoto-flat. This is a gameplay-visible change, since it shows less
        // ground per height; MinHeight and MaxHeight are deliberately left as
        // they are (V3-01 clause 2). MAP-05's height-driven shadow distance has
        // not landed, so BuildLightRig's DirectionalShadowMaxDistance stays a
        // constant 90 m, which the narrower frame only covers more completely.
        // The minimap frustum is projected from this camera's own rays
        // (SkirmishLive.GroundPoint uses ProjectRayNormal), so it tracks the new
        // FOV with no separate edit.
        Fov = 50f;
        _target = Position;
        // V3-02 (doc 25): the far tilt-shift DOF is removed rather than kept.
        // At 75 FOV its blur fell on the off-map void at the top of the frame
        // and cost a little for nothing; with V3-01 that region is now real
        // battlefield, and softening the far units and structures the player
        // must read is the same mistake V3-02 forbids in the near field. This is
        // the "DofBlurFarEnabled = false and reclaim the cost" branch. The dead
        // DofBlurFarDistance = 55f initialiser (overwritten every frame by the
        // old _Process) went with it. Auto-exposure stays off so muzzle flashes
        // never pump the frame.
        _attrs = new CameraAttributesPractical
        {
            DofBlurFarEnabled = false,
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
        // TICKET-P5-SET-01: pan is four rebindable InputMap actions, and the
        // literal WASD half is GONE rather than moved. It could not survive: doc
        // 18 Phase A wants A for attack-move and S for stop, and a key that both
        // pans the camera and orders the army does neither. The defaults are the
        // arrow keys, which with the graded edge band and the minimap is the
        // classic scheme; a player who wants WASD back rebinds these four rows
        // in the settings scene, and the conflict detector will make them move
        // attack-move and stop out of the way first, which is the honest order
        // to do it in. Input.IsActionPressed (not IsActionJustPressed): a pan is
        // held, and this is a frame poll rather than an input callback.
        var move = Vector3.Zero;
        if (Input.IsActionPressed("camera_forward")) move.Z -= 1;
        if (Input.IsActionPressed("camera_back")) move.Z += 1;
        if (Input.IsActionPressed("camera_left")) move.X -= 1;
        if (Input.IsActionPressed("camera_right")) move.X += 1;
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
        // V3-02: the per-frame far-DOF distance update went with the effect. The
        // camera no longer runs a depth of field, so there is nothing to drive.

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
