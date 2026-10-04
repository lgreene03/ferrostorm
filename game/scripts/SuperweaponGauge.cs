using Godot;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// P8-10: the superweapon readout. One row per superweapon the local seat may
/// know about: its own, always, and an enemy's once it has been SEEN (GDD s10's
/// "superweapon detected"). Each row is a name, a state and a charge bar.
///
/// It existed nowhere (FEEL-02): a player learned their 4000-credit weapon was
/// charged only by pressing F and reading a refusal, never heard it come
/// ready, and had no idea an enemy one was counting down. Every number here is
/// the SIM's: the charge and its total are handed in each frame, never kept or
/// predicted, because the full charge is a /data number now being retuned from
/// 1500 ticks to 5400 and a hardcoded duration would be wrong the day it lands.
///
/// A feed-only Control in the SupportPowerBar idiom: it reads nothing, issues
/// nothing, and is handed its rows by the scene.
/// </summary>
public partial class SuperweaponGauge : VBoxContainer
{
    /// <summary>One row. Charge and Total are ticks as the sim holds them;
    /// Strike is the launch countdown, -1 when nothing is in flight.</summary>
    public readonly record struct Row(int StructureId, string Name, bool Own, int Charge, int Total, int Strike)
    {
        /// <summary>The bar's fill, 0 empty to 1 charged: how much of the
        /// sim's own total has elapsed. A strike in flight reads full.</summary>
        public float Fill => Strike >= 0 || Total <= 0 ? 1f : Mathf.Clamp(1f - Charge / (float)Total, 0f, 1f);
    }

    private const float BarWidth = 210f;
    private const float BarHeight = 4f;
    private static readonly Color Bone = UplinkUi.Bone;
    private static readonly Color Gold = UplinkUi.FerriteGold;
    private static readonly Color Hostile = new(0.85f, 0.25f, 0.2f);   // the base-alert red
    private static readonly Color Trough = UplinkUi.Seam;

    private sealed class RowNodes
    {
        public Label Text = null!;
        public ColorRect Fill = null!;
    }

    private readonly List<Row> _rows = new();
    private readonly List<RowNodes> _nodes = new();

    public void Init()
    {
        Name = "SuperweaponGauge";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        AddThemeConstantOverride("separation", 2);
    }

    /// <summary>The per-frame handover. `launchKey` is the live binding's name,
    /// or null where the reader takes no orders (a replay), so a READY row
    /// names the key a player would press and never one they cannot.</summary>
    public void Refresh(IReadOnlyList<Row> rows, string? launchKey)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        while (_nodes.Count < _rows.Count) _nodes.Add(BuildRow());
        for (int i = 0; i < _nodes.Count; i++)
        {
            bool used = i < _rows.Count;
            var n = _nodes[i];
            n.Text.GetParent<Control>().Visible = used;
            if (!used) continue;
            var r = _rows[i];
            n.Text.Text = TextOf(r, launchKey);
            n.Fill.Size = new Vector2(BarWidth * r.Fill, BarHeight);
            n.Fill.Color = r.Own ? Gold : Hostile;
            // A charged weapon pulses so it is noticed without being read: the
            // one row that is a call to action (own) or a threat (enemy).
            bool ready = r.Charge == 0 && r.Strike < 0;
            float pulse = ready ? 0.65f + 0.35f * Mathf.Sin((float)(Time.GetTicksMsec() / 1000.0) * 6f) : 1f;
            n.Text.Modulate = new Color(1, 1, 1, pulse);
        }
        Visible = _rows.Count > 0;
    }

    /// <summary>What a row says. Pure, so the harness can read the same words
    /// the label shows.</summary>
    public static string TextOf(in Row r, string? launchKey)
    {
        string who = r.Own ? r.Name : $"ENEMY {r.Name}";
        if (r.Strike >= 0)
            return r.Own
                ? $"{who}   STRIKE IN {Seconds(r.Strike)}s"
                : $"{who}   INBOUND {Seconds(r.Strike)}s";
        if (r.Charge == 0)
            return r.Own && launchKey != null ? $"{who}   READY   [{launchKey}]" : $"{who}   READY";
        return r.Own ? $"{who}   CHARGING {Clock(r.Charge)}" : $"{who}   {Clock(r.Charge)}";
    }

    private static int Seconds(int ticks) => Mathf.CeilToInt(ticks / (float)Ferrostorm.Sim.World.TicksPerSecond);

    /// <summary>m:ss, rounded up, so "0:00" is never shown while charge remains.</summary>
    public static string Clock(int ticks)
    {
        int s = Seconds(ticks);
        return $"{s / 60}:{s % 60:00}";
    }

    private RowNodes BuildRow()
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 1);
        var text = new Label { MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeFontSizeOverride("font_size", 12);
        text.AddThemeColorOverride("font_color", Bone);
        box.AddChild(text);
        var trough = new ColorRect
        {
            Color = Trough,
            CustomMinimumSize = new Vector2(BarWidth, BarHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var fill = new ColorRect { Size = new Vector2(0, BarHeight), MouseFilter = MouseFilterEnum.Ignore };
        trough.AddChild(fill);
        box.AddChild(trough);
        AddChild(box);
        return new RowNodes { Text = text, Fill = fill };
    }

    // ---- Verification surface: what the gauge SHOWS, never a recomputation.

    public int RowCount => _rows.Count;
    public int IndexOf(int structureId)
    {
        for (int i = 0; i < _rows.Count; i++) if (_rows[i].StructureId == structureId) return i;
        return -1;
    }
    public string RowText(int i) => _nodes[i].Text.Text;
    /// <summary>The drawn fill as a fraction of the trough, read off the node.</summary>
    public float RowFillDrawn(int i) => _nodes[i].Fill.Size.X / BarWidth;
    public bool RowOwn(int i) => _rows[i].Own;
}
