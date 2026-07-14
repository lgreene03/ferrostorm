using Godot;
using Ferrostorm.Sim;

namespace Ferrostorm.Client;

/// <summary>
/// Classic shroud: a transparent plane floated above the battlefield whose
/// texture is rebuilt each tick from the sim's per-player visibility
/// bitgrids (public read API only). Unexplored cells are near-black,
/// explored-but-unseen cells are dimmed, visible cells are clear. Linear
/// filtering gives the soft fog edge. The same image feeds the minimap.
/// </summary>
public partial class FogOfWar : Node3D
{
    private int _w, _h;
    private Image _img = null!;
    private ImageTexture _tex = null!;
    public Image FogImage => _img;

    public void Init(int w, int h)
    {
        _w = w; _h = h;
        _img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        _img.Fill(new Color(0.01f, 0.012f, 0.016f, 0.97f));
        _tex = ImageTexture.CreateFromImage(_img);
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = _tex,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(w, h) },
            Position = new Vector3(w / 2f, 3.2f, h / 2f),
            MaterialOverride = mat,
            Name = "Shroud",
        });
    }

    /// <summary>Rebuild from the sim's bitgrids. Called once per sim tick,
    /// after Step, read-only.</summary>
    public void UpdateFrom(World world, int player)
    {
        for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                if (world.IsVisible(player, x, y))
                    _img.SetPixel(x, y, new Color(0, 0, 0, 0));
                else if (world.IsExplored(player, x, y))
                    _img.SetPixel(x, y, new Color(0.01f, 0.012f, 0.016f, 0.55f));
                else
                    _img.SetPixel(x, y, new Color(0.01f, 0.012f, 0.016f, 0.97f));
            }
        _tex.Update(_img);
    }
}
