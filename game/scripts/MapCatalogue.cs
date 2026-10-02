using Godot;
using Ferrostorm.Sim;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// P8-40: what the theatre picker knows about one map before a player chooses
/// it. Read from the map's own header, so a map cannot be listed under a name,
/// size or seat count it does not declare.
/// </summary>
public sealed record MapCard(string Path, string Stem, string HeaderName, int Width, int Height, int Seats)
{
    /// <summary>The name a player reads. A map whose header carries no name is
    /// still listed, under its file name, rather than hidden; the harness
    /// fails any SHIPPED map that falls back, because the fallback is exactly
    /// the bare file name this card exists to replace.</summary>
    public string DisplayName => (HeaderName.Length > 0 ? HeaderName : Stem).ToUpperInvariant();
    public string SizeText => $"{Width} x {Height}";
    public string SeatsText => Seats == 1 ? "1 SEAT" : $"{Seats} SEATS";
}

/// <summary>
/// P8-40: the shipped skirmish maps as the menu presents them, and a thumbnail
/// of each drawn from its own terrain grid.
///
/// THE NAME is the map's first header comment, up to its first full stop or
/// comma: every generated map opens "# Serpentine Ford. The Serpentine river
/// winds..." or "# Karsthollow Basin, the epic theatre." and the tools/gen_*
/// scripts write that line, so the convention is the one the pool already
/// follows rather than a new key. MapLoader skips comment lines, which is why
/// adding one (skirmish-03 had none) moves no hash; P8-40 measured that rather
/// than trusting it.
///
/// THE THUMBNAIL is generated at runtime, never authored, so there is no art
/// asset to maintain and it cannot go stale against the map file: it is parsed
/// by the sim's own MapData, the same grid the battle is built from, at one
/// pixel per cell, and cached per map path and file timestamp. It is built
/// only when a map is shown, so opening the menu reads nine headers and draws
/// one thumbnail.
/// </summary>
public static class MapCatalogue
{
    // Terrain classes, in the uplink palette. Bridges and destroyable spans
    // stay OPEN ground, because that is what they are while they stand, and
    // drawn that way the river shows its crossings as gaps.
    public static readonly Color OpenGround = new(0.20f, 0.205f, 0.19f);
    public static readonly Color BlockedGround = new(0.42f, 0.41f, 0.39f);
    public static readonly Color Water = new(0.16f, 0.32f, 0.50f);
    public static readonly Color Ferrite = UplinkUi.FerriteGold;
    public static readonly Color StartMark = UplinkUi.Bone;

    /// <summary>Every shipped skirmish map, in file order. "skirmish-*" and
    /// not "*": data/maps also holds test fixtures (the four-start multiseat
    /// fixture says in its own header it must never be offered as a theatre).
    /// SORTED, because Directory.GetFiles promises no order and the picker
    /// listed the pool in whatever order the file system returned.</summary>
    public static List<MapCard> Skirmish()
    {
        var files = new List<string>(System.IO.Directory.GetFiles(
            System.IO.Path.Combine(GameFiles.RepoRoot, "data", "maps"), "skirmish-*.fmap"));
        files.Sort(System.StringComparer.Ordinal);
        var cards = new List<MapCard>(files.Count);
        foreach (string f in files) cards.Add(Read(f));
        return cards;
    }

    /// <summary>The header only, stopping at "grid:": the menu has no use for
    /// the grid until it draws a thumbnail. A malformed file gives a card of
    /// zero size and zero seats rather than an exception in the menu; the
    /// battle scene's own load is what refuses it, with a readable notice.</summary>
    public static MapCard Read(string path)
    {
        string stem = System.IO.Path.GetFileNameWithoutExtension(path);
        string name = "";
        int w = 0, h = 0, seats = 0;
        try
        {
            bool first = true;
            foreach (string raw in System.IO.File.ReadLines(path))
            {
                string line = raw.Trim();
                if (first)
                {
                    first = false;
                    if (line != "ferrostorm-map v1" && line != "ferrostorm-map v2") break;
                    continue;
                }
                if (line == "grid:") break;
                if (line.StartsWith('#'))
                {
                    if (name.Length == 0) name = NameFrom(line);
                    continue;
                }
                var p = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 3 && p[0] == "size")
                {
                    int.TryParse(p[1], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out w);
                    int.TryParse(p[2], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out h);
                }
                else if (p.Length >= 1 && p[0] == "start") seats++;
            }
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"map picker: could not read {path}: {e.Message}");
        }
        return new MapCard(path, stem, name, w, h, seats);
    }

    /// <summary>"# Serpentine Ford. The river..." gives "Serpentine Ford".</summary>
    public static string NameFrom(string commentLine)
    {
        string text = commentLine.TrimStart('#').Trim();
        int cut = text.IndexOfAny(new[] { '.', ',' });
        return (cut >= 0 ? text[..cut] : text).Trim();
    }

    private static readonly Dictionary<string, (System.DateTime Stamp, Image Image, ImageTexture Texture)> Cache = new();

    /// <summary>The thumbnail texture for a map, built on first use and kept
    /// until the file changes. Null if the map does not parse, so a broken
    /// file costs the picker its preview and never the menu.</summary>
    public static ImageTexture? Thumbnail(string path) => Entry(path)?.Texture;

    /// <summary>The CPU-side image behind the texture, for the harness to read
    /// pixels from: a headless renderer keeps no texture data to read back.</summary>
    public static Image? ThumbnailImage(string path) => Entry(path)?.Image;

    private static (System.DateTime Stamp, Image Image, ImageTexture Texture)? Entry(string path)
    {
        System.DateTime stamp;
        try { stamp = System.IO.File.GetLastWriteTimeUtc(path); }
        catch (System.Exception) { return null; }
        if (Cache.TryGetValue(path, out var hit) && hit.Stamp == stamp) return hit;
        try
        {
            var image = BuildThumbnail(MapData.Load(path));
            var entry = (stamp, image, ImageTexture.CreateFromImage(image));
            Cache[path] = entry;
            return entry;
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"map picker: no thumbnail for {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>One pixel per cell, coloured by terrain class, with each start
    /// position marked by a square big enough to read at menu scale. Written
    /// into one byte buffer and handed over whole rather than set pixel by
    /// pixel, which on the 256 x 192 theatre is the difference between one
    /// engine call and forty-nine thousand.</summary>
    public static Image BuildThumbnail(MapData map)
    {
        int w = map.Width, h = map.Height;
        var px = new byte[w * h * 3];
        void Put(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = (y * w + x) * 3;
            px[i] = (byte)c.R8; px[i + 1] = (byte)c.G8; px[i + 2] = (byte)c.B8;
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) Put(x, y, OpenGround);
        foreach (var (cx, cy) in map.Blocked)
            Put(cx, cy, map.Visual.TryGetValue((cx, cy), out char ch) && ch == 'w' ? Water : BlockedGround);
        foreach (var (cx, cy) in map.Fields) Put(cx, cy, Ferrite);
        int r = StartMarkRadius(map);
        foreach (var (sx, sy) in map.Starts.Values)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++) Put(sx + dx, sy + dy, StartMark);
        return Image.CreateFromData(w, h, false, Image.Format.Rgb8, px);
    }

    /// <summary>Half the side of a start marker, scaled with the map so the
    /// marker reads the same size whatever the theatre: 5 cells across on a
    /// 96 x 64 map, 13 on the 256 x 192 one.</summary>
    public static int StartMarkRadius(MapData map) => System.Math.Max(2, System.Math.Min(map.Width, map.Height) / 32);
}
