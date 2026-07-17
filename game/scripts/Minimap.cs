using Godot;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// Bottom-left minimap: terrain and ridges as a static base, the live fog
/// image as shroud, entity dots (own always, enemies only when visible),
/// the camera look-at point, and click or drag to fly the camera. Reads
/// everything through data the scene hands it each frame.
/// </summary>
public partial class Minimap : Control
{
    private const float SizePx = 168f;
    private int _w, _h;
    private Image _base = null!;
    private TextureRect _baseRect = null!;
    private TextureRect _fogRect = null!;
    private readonly List<(Vector2 Pos, Color C)> _dots = new();
    // W3-20: expiring alert pings and the camera view-frustum trapezoid
    // (which replaces the old camera-dot circle).
    private readonly List<(Vector2 Pos, Color C, double T0)> _pings = new();
    private Vector2[]? _frustum;
    private System.Action<Vector2>? _onNavigate;
    // ADR-008 clause 4: the blackout. False until the scene says otherwise,
    // because the classic rule is that the minimap must be EARNED: no living
    // Radar Uplink with supply covering draw means no radar view.
    private bool _radarLive;

    /// <summary>ADR-008 clause 4, per doc 22 BD-09's sketch: while dark the
    /// minimap renders only the cinder panel and centred bone RADAR OFFLINE
    /// text - no terrain, no fog, no dots, no frustum. Pings still render,
    /// deliberately: blanking the base-under-attack ping would make the
    /// blackout a stealth nerf to the alert system (doc 22's LOW_POWER
    /// integration note - the alert most likely to fire while dark must stay
    /// visible). Clicks stop navigating while dark: a map the player cannot
    /// see should not silently order the camera around, so the click is
    /// swallowed rather than passed to the battlefield beneath.</summary>
    public void SetRadarLive(bool live)
    {
        if (_radarLive == live) return;
        _radarLive = live;
        _baseRect.Visible = live;
        _fogRect.Visible = live;
        QueueRedraw();
    }

    /// <summary>Verification read: what the blackout gate is actually showing.</summary>
    public bool RadarLiveShown => _radarLive;

    /// <summary>W3-20: drop an expanding alert ping at a world position.
    /// Pings pulse for 2.4s; Refresh's QueueRedraw animates them for free.</summary>
    public void Ping(Vector2 world, Color c)
        => _pings.Add((new Vector2(world.X / _w, world.Y / _h) * Size, c, Time.GetTicksMsec() / 1000.0));

    public void Init(int w, int h, IEnumerable<(int X, int Y)> blocked, Image fogImage, System.Action<Vector2> onNavigate)
    {
        _w = w; _h = h;
        _onNavigate = onNavigate;
        AnchorTop = 1; AnchorBottom = 1;
        OffsetTop = -(SizePx + 44); OffsetLeft = 12;
        CustomMinimumSize = new Vector2(SizePx, SizePx * h / w);
        Size = CustomMinimumSize;
        // W3-20: the camera frustum can extend past the map edge; clip the
        // polyline (and everything else drawn here) to the minimap rect.
        ClipContents = true;

        _base = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        _base.Fill(new Color(0.10f, 0.105f, 0.12f));
        foreach (var (bx, by) in blocked)
            _base.SetPixel(bx, by, new Color(0.30f, 0.32f, 0.36f));
        // W3-20 find: children render on top of the control's own _Draw, so
        // the terrain and fog layers were burying the dots, pings and
        // frustum. ShowBehindParent keeps their relative order (fog over
        // terrain) but puts both beneath everything _Draw paints.
        _baseRect = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(_base),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            AnchorRight = 1, AnchorBottom = 1,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };
        AddChild(_baseRect);
        _fogRect = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(fogImage),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            AnchorRight = 1, AnchorBottom = 1,
            MouseFilter = MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };
        AddChild(_fogRect);
        // ADR-008: the map starts dark until the scene's first radar-live
        // computation says otherwise - every shipped map opens radarless, and
        // one lit frame before the first Refresh would flash the terrain.
        _baseRect.Visible = _radarLive;
        _fogRect.Visible = _radarLive;
    }

    /// <summary>camAt is retained for call-site stability; the W3-20 frustum
    /// trapezoid replaces the old camera circle as the view indicator.</summary>
    public void Refresh(Image fogImage, IEnumerable<(float X, float Y, Color C)> dots, Vector2 camAt, Vector2[]? frustum = null)
    {
        (_fogRect.Texture as ImageTexture)?.Update(fogImage);
        _dots.Clear();
        foreach (var (x, y, c) in dots)
            _dots.Add((new Vector2(x / _w, y / _h) * Size, c));
        if (frustum is { Length: 4 })
        {
            _frustum = new Vector2[4];
            for (int i = 0; i < 4; i++)
                _frustum[i] = new Vector2(frustum[i].X / _w, frustum[i].Y / _h) * Size;
        }
        else
            _frustum = null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_radarLive)
        {
            // The blackout face: cinder panel, bone text, nothing else. The
            // colours are doc 16 tokens (UplinkUi.Panel and UplinkUi.Bone).
            DrawRect(new Rect2(Vector2.Zero, Size), UplinkUi.Panel);
            var font = GetThemeDefaultFont();
            if (font != null)
                DrawString(font, new Vector2(0, Size.Y / 2 + 4), "RADAR OFFLINE",
                    HorizontalAlignment.Center, Size.X, 12, UplinkUi.Bone);
        }
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.79f, 0.63f, 0.36f, 0.7f), false, 1f);
        // W3-20: alert pings - expanding rings cycling every 0.8s, fading
        // over their 2.4s life. Pings render in BOTH radar states (ADR-008).
        double now = Time.GetTicksMsec() / 1000.0;
        for (int i = _pings.Count - 1; i >= 0; i--)
        {
            double age = now - _pings[i].T0;
            if (age > 2.4) { _pings.RemoveAt(i); continue; }
            float cyc = (float)(age % 0.8) / 0.8f;
            float r = 2f + cyc * 10f;
            float a = (1f - cyc) * (1f - (float)age / 2.4f);
            DrawArc(_pings[i].Pos, r, 0, Mathf.Tau, 20, _pings[i].C with { A = a }, 1.5f);
        }
        if (!_radarLive) return; // no dots, no frustum: the radar view is earned
        foreach (var (pos, c) in _dots)
            DrawRect(new Rect2(pos - new Vector2(1.5f, 1.5f), new Vector2(3, 3)), c);
        if (_frustum is { Length: 4 } f)
            DrawPolyline(new[] { f[0], f[1], f[2], f[3], f[0] }, new Color(0.9f, 0.88f, 0.8f, 0.8f), 1f);
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (!_radarLive)
        {
            // Swallow rather than fall through: an unswallowed click on a dark
            // minimap would reach the battlefield as a stray order.
            if (ev is InputEventMouseButton or InputEventMouseMotion) AcceptEvent();
            return;
        }
        if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
            Navigate(mb.Position);
        else if (ev is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Left) != 0)
            Navigate(mm.Position);
    }

    private void Navigate(Vector2 local)
    {
        var world = new Vector2(local.X / Size.X * _w, local.Y / Size.Y * _h);
        _onNavigate?.Invoke(world);
        AcceptEvent();
    }
}
