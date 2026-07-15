using Godot;
using System;
using System.IO;

namespace Ferrostorm.Client;

/// <summary>
/// TICKET-P5-SAVE-01: the in-battle menu. P already paused the skirmish and
/// showed a banner; it now raises this overlay instead, which is where saving,
/// loading and abandoning live. Three pages (root, save slots, load slots)
/// share one scrim, in the MainMenu overlay idiom.
/// </summary>
public partial class PauseMenu : Control
{
    private SkirmishLive _game = null!;
    private ColorRect _overlay = null!;
    private Control _pageRoot = null!;
    private string _flash = "";

    public void Init(SkirmishLive game)
    {
        _game = game;
        AnchorRight = 1; AnchorBottom = 1;
        // Semi-opaque, not the menu's 0.97: the player is pausing a battle and
        // wants to see the battle they are pausing.
        _overlay = UplinkUi.FullOverlay(this, 0.82f);
        _pageRoot = new Control { Name = "Page", AnchorRight = 1, AnchorBottom = 1 };
        _overlay.AddChild(_pageRoot);
        ShowRoot();
    }

    /// <summary>Swap the page in place. The outgoing children are hidden as well
    /// as freed: QueueFree only lands at the end of the frame, and a stale page
    /// drawn under the new one for even one frame is a visible flicker.</summary>
    private VBoxContainer Page(string heading, int halfW = 280, int halfH = 210)
    {
        foreach (var c in _pageRoot.GetChildren())
        {
            if (c is Control ctl) { ctl.Visible = false; ctl.MouseFilter = MouseFilterEnum.Ignore; }
            c.QueueFree();
        }
        var host = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
        _pageRoot.AddChild(host);
        return UplinkUi.OverlayBox(host, heading, halfW, halfH);
    }

    private void ShowRoot()
    {
        var v = Page("OPERATIONS", 280, 210);
        v.AddChild(UplinkUi.Note(_game.ModeLine()));
        v.AddChild(new HSeparator());
        v.AddChild(UplinkUi.MenuButton("RESUME", () => _game.ClosePause()));
        v.AddChild(UplinkUi.MenuButton("SAVE GAME", () => ShowSlots(saving: true), enabled: _game.CanSave));
        v.AddChild(UplinkUi.MenuButton("LOAD GAME", () => ShowSlots(saving: false)));
        v.AddChild(UplinkUi.MenuButton("ABANDON OPERATION", () => _game.QuitToMenu()));
        if (_flash.Length > 0) v.AddChild(UplinkUi.Note(_flash, 13));
        if (!_game.CanSave)
            v.AddChild(UplinkUi.Note("saving is disabled during replay playback", 11));
    }

    private void ShowSlots(bool saving)
    {
        var v = Page(saving ? "SAVE OPERATION" : "LOAD OPERATION", 320, 230);
        for (int i = 1; i <= GameFiles.SlotCount; i++)
        {
            int slot = i;
            var meta = MatchMeta.Read(GameFiles.SlotMeta(slot));
            bool occupied = meta != null && File.Exists(GameFiles.SlotSave(slot));
            string label = $"SLOT {slot}   " + (occupied ? meta!.Line() : "EMPTY");
            v.AddChild(UplinkUi.MenuButton(label,
                saving ? () => DoSave(slot) : () => DoLoad(slot),
                enabled: saving || occupied));
        }
        v.AddChild(new HSeparator());
        if (saving)
            v.AddChild(UplinkUi.Note("a save captures the world exactly; the skirmish AI's own wave timers are not in the save file and restart on load", 11));
        else
            v.AddChild(UplinkUi.Note("loading restarts the battle scene from the saved tick and ends the current replay recording", 11));
        v.AddChild(UplinkUi.MenuButton("BACK", ShowRoot));
    }

    private void DoSave(int slot)
    {
        try
        {
            _game.SaveToSlot(slot);
            _flash = $"SAVED TO SLOT {slot}";
        }
        catch (Exception e)
        {
            GD.PushError($"save to slot {slot} failed: {e}");
            _flash = $"SLOT {slot} SAVE FAILED - SEE LOG";
        }
        ShowRoot();
    }

    private void DoLoad(int slot)
    {
        var meta = MatchMeta.Read(GameFiles.SlotMeta(slot));
        if (meta is null) return;
        _game.LoadFromSlot(slot, meta);
    }
}
