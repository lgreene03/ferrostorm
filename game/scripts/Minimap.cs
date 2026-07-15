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
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.79f, 0.63f, 0.36f, 0.7f), false, 1f);
        // W3-20: alert pings - expanding rings cycling every 0.8s, fading
        // over their 2.4s life.
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
        foreach (var (pos, c) in _dots)
            DrawRect(new Rect2(pos - new Vector2(1.5f, 1.5f), new Vector2(3, 3)), c);
        if (_frustum is { Length: 4 } f)
            DrawPolyline(new[] { f[0], f[1], f[2], f[3], f[0] }, new Color(0.9f, 0.88f, 0.8f, 0.8f), 1f);
    }

    public override void _GuiInput(InputEvent ev)
    {
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
