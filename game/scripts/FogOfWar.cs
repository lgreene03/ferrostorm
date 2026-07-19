using Godot;
using Ferrostorm.Sim;

namespace Ferrostorm.Client;

/// <summary>
/// Classic shroud: a transparent plane floated above the battlefield whose
/// texture is rebuilt each tick from the sim's per-player visibility
/// bitgrids (public read API only). Unexplored cells are near-black,
/// explored-but-unseen cells are dimmed, visible cells are clear. The same
/// TRUTH image feeds the minimap; the plane gets a softened copy (V2-00).
/// </summary>
public partial class FogOfWar : Node3D
{
    private int _w, _h;

    // The truth image: exactly one texel per map cell, nearest-neighbour
    // faithful to World.IsVisible / World.IsExplored. This is what the
    // minimap reads and it is deliberately NOT softened.
    private Image _img = null!;
    public Image FogImage => _img;

    // The softened image the 3D plane samples. Same resolution; the fix is
    // the shape of the field, not its size. See SoftenInto.
    private Image _soft = null!;
    private ImageTexture _tex = null!;

    // Scratch buffers, allocated once. UpdateFrom runs every sim tick, so
    // nothing in this file may allocate per call.
    private float[] _a = null!;      // true opacity per cell
    private float[] _s = null!;      // working opacity per cell
    private float[] _t = null!;      // separable-blur transpose scratch
    private byte[] _truth = null!;   // RGBA8 for _img
    private byte[] _softPx = null!;  // RGBA8 for _soft

    // Named Clear rather than Visible because Node3D.Visible exists and
    // shadowing it compiles with a warning, and this project builds at zero.
    private static readonly Color Clear = new(0f, 0f, 0f, 0f);
    // C-08 (doc 22, ratified) scheduled as V1-02 (doc 25). Most of the frame
    // is explored-but-unseen at any moment in a real match, so a 38 per cent
    // near-black overlay was a second uniform veil stacked on the volumetric
    // fog's. The VALUES are doc 22's and are not re-derived here. If MAP-07's
    // byte-array rewrite ever lands these become bytes 5, 8, 13, 77.
    private static readonly Color Explored = new(0.020f, 0.030f, 0.052f, 0.30f);
    private static readonly Color Unexplored = new(0.008f, 0.012f, 0.022f, 0.985f);

    public void Init(int w, int h)
    {
        _w = w; _h = h;
        int n = w * h;
        _a = new float[n];
        _s = new float[n];
        _t = new float[n];
        _truth = new byte[n * 4];
        _softPx = new byte[n * 4];

        _img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        _img.Fill(Unexplored);
        _soft = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        _soft.Fill(Unexplored);
        _tex = ImageTexture.CreateFromImage(_soft);

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
                int i = y * _w + x;
                Color c = world.IsVisible(player, x, y) ? Clear
                        : world.IsExplored(player, x, y) ? Explored
                        : Unexplored;
                _a[i] = c.A;
                int o = i * 4;
                _truth[o] = (byte)(c.R * 255f + 0.5f);
                _truth[o + 1] = (byte)(c.G * 255f + 0.5f);
                _truth[o + 2] = (byte)(c.B * 255f + 0.5f);
                _truth[o + 3] = (byte)(c.A * 255f + 0.5f);
            }
        _img.SetData(_w, _h, false, Image.Format.Rgba8, _truth);
        SoftenInto();
        _soft.SetData(_w, _h, false, Image.Format.Rgba8, _softPx);
        _tex.Update(_soft);
    }

    // ------------------------------------------------------------------
    // V2-00 (doc 25 wave V2, folded in from the V0/V1 delivery notes' first
    // "needed next"): the shroud boundary reads as a staircase of squares.
    //
    // The cause is not the filter. The plane is already sampled bilinear;
    // one texel per map cell means bilinear interpolates a HARD 0-to-0.30
    // step across roughly thirty screen pixels at CAM-A, and a bilinear ramp
    // over a hard step is exactly a visible staircase with diamond facets at
    // the corners. Raising the texture resolution alone would not help: it
    // would give the same hard step more texels to be hard in. The fix is to
    // soften the FIELD, after which the existing bilinear upsample has
    // something smooth to interpolate and the boundary reads as an edge of
    // vision. Resolution is therefore left alone, which also keeps this a
    // free change: this method is cheaper than the SetPixel loop it replaces.
    //
    // HOW THIS IS GUARANTEED NOT TO REVEAL ANYTHING. Two independent locks.
    //
    // 1. Nothing in the shroud plane hides anything in the first place. It is
    //    an unshaded alpha wash. What actually hides an enemy is
    //    SkirmishLive.SyncActors, which sets node.Visible from
    //    World.IsVisible directly (SkirmishLive.cs, the PlayerId == 1 gate),
    //    and the minimap dot list applies the same gate. Neither reads this
    //    texture. The sim's visibility truth is untouched by this file and
    //    this change does not go near it.
    //
    // 2. The softened field is nevertheless constructed so that it can only
    //    ever be MORE opaque than the truth, never less, at every texel:
    //      a. dilate opacity over a 3x3 neighbourhood, which is an EROSION of
    //         the visible set, so the softening starts one cell inside known
    //         visibility rather than one cell outside it;
    //      b. blur the dilated field;
    //      c. clamp the result to max(blurred, true) per cell.
    //    Step (c) makes the invariant unconditional: for every cell,
    //    final opacity >= true opacity. So the ramp lives entirely on the
    //    visible side of the boundary. It feathers INWARD, dimming the outer
    //    ring of ground the player can legitimately see, and it never lifts
    //    a single texel of shroud off ground the player cannot. That is the
    //    direction the ticket permits and it is the conservative one.
    //
    // Colour is blurred alongside opacity for a clean gradient. Colour cannot
    // reveal anything on its own: opacity is what decides how much ground
    // shows through, and opacity is clamped.
    // ------------------------------------------------------------------
    private void SoftenInto()
    {
        // (a) dilate opacity: 3x3 maximum. Erodes the visible set by one cell.
        for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                float m = 0f;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= _h) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= _w) continue;
                        float v = _a[yy * _w + xx];
                        if (v > m) m = v;
                    }
                }
                _s[y * _w + x] = m;
            }

        // (b) blur: two passes of a 3-tap separable box, which is a 5-cell
        // tent. Wide enough that the ramp spans about four cells and reads as
        // soft at CAM-A, narrow enough that it does not eat a tactically
        // useful amount of the visible region.
        BlurX(); BlurY(); BlurX(); BlurY();

        // (c) clamp to the truth and write the pixels.
        for (int i = 0; i < _a.Length; i++)
        {
            float op = _s[i];
            float tr = _a[i];
            if (op < tr) op = tr;           // THE INVARIANT: never less opaque
            // Colour: interpolate the two shroud tints by how opaque this
            // cell ended up. Below the explored alpha there is nothing to
            // tint toward but the explored colour itself.
            float k = op <= Explored.A ? 0f
                    : (op - Explored.A) / (Unexplored.A - Explored.A);
            int o = i * 4;
            _softPx[o] = (byte)((Explored.R + (Unexplored.R - Explored.R) * k) * 255f + 0.5f);
            _softPx[o + 1] = (byte)((Explored.G + (Unexplored.G - Explored.G) * k) * 255f + 0.5f);
            _softPx[o + 2] = (byte)((Explored.B + (Unexplored.B - Explored.B) * k) * 255f + 0.5f);
            _softPx[o + 3] = (byte)(op * 255f + 0.5f);
        }
    }

    private void BlurX()
    {
        for (int y = 0; y < _h; y++)
        {
            int row = y * _w;
            for (int x = 0; x < _w; x++)
            {
                float l = _s[row + (x > 0 ? x - 1 : 0)];
                float c = _s[row + x];
                float r = _s[row + (x < _w - 1 ? x + 1 : _w - 1)];
                _t[row + x] = (l + c + r) * (1f / 3f);
            }
        }
        (_s, _t) = (_t, _s);
    }

    private void BlurY()
    {
        for (int y = 0; y < _h; y++)
        {
            int row = y * _w;
            int up = (y > 0 ? y - 1 : 0) * _w;
            int dn = (y < _h - 1 ? y + 1 : _h - 1) * _w;
            for (int x = 0; x < _w; x++)
                _t[row + x] = (_s[up + x] + _s[row + x] + _s[dn + x]) * (1f / 3f);
        }
        (_s, _t) = (_t, _s);
    }
}
