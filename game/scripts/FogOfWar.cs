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
        _img.Fill(new Color(0.008f, 0.012f, 0.022f, 0.985f));
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
                    // C-08 (doc 22, ratified) scheduled as V1-02 (doc 25).
                    // Most of the frame is explored-but-unseen at any moment in
                    // a real match, so a 38 per cent near-black overlay was a
                    // second uniform veil stacked on the volumetric fog's. The
                    // lower alpha paints out less of the ground's already
                    // scarce chroma and the stronger blue lift makes what
                    // remains read as distance haze rather than as black paint.
                    // Doc 25 raises this ticket's impact from LOW to HIGH; the
                    // VALUES are doc 22's and are not re-derived here.
                    // If MAP-07's byte-array rewrite ever lands, these become
                    // bytes 5, 8, 13, 77 and the two must not disagree.
                    _img.SetPixel(x, y, new Color(0.020f, 0.030f, 0.052f, 0.30f));
                else
                    _img.SetPixel(x, y, new Color(0.008f, 0.012f, 0.022f, 0.985f));
            }
        _tex.Update(_img);
    }
}
