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
    private Vector2 _camDot;
    private System.Action<Vector2>? _onNavigate;

    public void Init(int w, int h, IEnumerable<(int X, int Y)> blocked, Image fogImage, System.Action<Vector2> onNavigate)
    {
        _w = w; _h = h;
        _onNavigate = onNavigate;
        AnchorTop = 1; AnchorBottom = 1;
        OffsetTop = -(SizePx + 44); OffsetLeft = 12;
        CustomMinimumSize = new Vector2(SizePx, SizePx * h / w);
        Size = CustomMinimumSize;

        _base = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        _base.Fill(new Color(0.10f, 0.105f, 0.12f));
        foreach (var (bx, by) in blocked)
            _base.SetPixel(bx, by, new Color(0.30f, 0.32f, 0.36f));
        _baseRect = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(_base),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            AnchorRight = 1, AnchorBottom = 1,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_baseRect);
        _fogRect = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(fogImage),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            AnchorRight = 1, AnchorBottom = 1,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_fogRect);
    }

    public void Refresh(Image fogImage, IEnumerable<(float X, float Y, Color C)> dots, Vector2 camAt)
    {
        (_fogRect.Texture as ImageTexture)?.Update(fogImage);
        _dots.Clear();
        foreach (var (x, y, c) in dots)
            _dots.Add((new Vector2(x / _w, y / _h) * Size, c));
        _camDot = new Vector2(camAt.X / _w, camAt.Y / _h) * Size;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.79f, 0.63f, 0.36f, 0.7f), false, 1f);
        foreach (var (pos, c) in _dots)
            DrawRect(new Rect2(pos - new Vector2(1.5f, 1.5f), new Vector2(3, 3)), c);
        DrawArc(_camDot, 7f, 0, Mathf.Tau, 16, new Color(0.9f, 0.88f, 0.8f, 0.8f), 1f);
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
