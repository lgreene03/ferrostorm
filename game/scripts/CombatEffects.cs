using Godot;
using Ferrostorm.Sim;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// CombatEffects: transient battlefield effects driven by the sim's per-tick
/// event list (TICKET-P2-SIM-13 events). The scene adds one instance and calls
/// OnTickEvents after each Step. Everything here is deliberately cheap: all
/// meshes and materials are shared static resources, so each effect allocates
/// only a handful of nodes, and every effect frees itself via a Tween bound to
/// its own node (killed automatically if the node dies early).
/// </summary>
public partial class CombatEffects : Node3D
{
    private static readonly Color Orange = new(1.0f, 0.55f, 0.15f);
    private static readonly Color Ember = new(1.0f, 0.35f, 0.08f);

    // Shared materials (static: never one resource per effect).
    private static readonly StandardMaterial3D MuzzleMat = Emissive(Orange, 3.0f, billboard: true);
    private static readonly StandardMaterial3D TracerMat = Emissive(new Color(1.0f, 0.8f, 0.4f), 2.5f);
    private static readonly StandardMaterial3D FlashMat = Emissive(Ember, 2.0f);
    private static readonly StandardMaterial3D RingMat = Emissive(Orange, 2.0f);
    private static readonly StandardMaterial3D ColumnMat = Emissive(new Color(1.0f, 0.75f, 0.35f), 3.0f);
    private static readonly StandardMaterial3D SmokeMat = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = new Color(0.25f, 0.24f, 0.22f, 0.7f),
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
    };

    // Shared meshes.
    private static readonly QuadMesh MuzzleMesh = new() { Size = new Vector2(0.5f, 0.5f) };
    private static readonly BoxMesh TracerMesh = new() { Size = Vector3.One };
    private static readonly SphereMesh FlashMesh = new() { Radius = 0.5f, Height = 1.0f, RadialSegments = 12, Rings = 6 };
    private static readonly TorusMesh RingMesh = new() { InnerRadius = 0.85f, OuterRadius = 1.0f };
    private static readonly CylinderMesh ColumnMesh = new() { TopRadius = 0.5f, BottomRadius = 0.9f, Height = 12.0f, RadialSegments = 10 };
    private static readonly QuadMesh SmokeQuad = MakeSmokeQuad();
    private static readonly ParticleProcessMaterial SmokeProcess = new()
    {
        Direction = new Vector3(0, 1, 0),
        Spread = 35.0f,
        InitialVelocityMin = 0.6f,
        InitialVelocityMax = 1.4f,
        Gravity = new Vector3(0, 0.5f, 0),
        ScaleMin = 0.6f,
        ScaleMax = 1.2f,
    };

    private static QuadMesh MakeSmokeQuad()
    {
        var q = new QuadMesh { Size = new Vector2(0.5f, 0.5f) };
        q.Material = SmokeMat;
        return q;
    }

    private static StandardMaterial3D Emissive(Color c, float energy, bool billboard = false) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
        BillboardMode = billboard ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.Disabled,
    };

    private static float ToF(Fix64 v) => (float)(v.Raw / 4294967296.0);

    /// <summary>Consume one tick's worth of sim events and spawn effects.</summary>
    public void OnTickEvents(IReadOnlyList<GameEvent> events, IReadOnlyDictionary<int, Node3D> actors, AudioDirector audio)
    {
        if (events == null || actors == null) return;
        foreach (var ev in events)
        {
            switch (ev.Type)
            {
                case GameEventType.Fired: OnFired(ev, actors, audio); break;
                case GameEventType.Died: OnDied(ev, actors, audio); break;
                case GameEventType.SuperweaponImpact: OnSuperweaponImpact(ev, audio); break;
                case GameEventType.ProductionComplete: audio?.Play("production_done", -6.0f); break;
                case GameEventType.SuperweaponLaunched: audio?.Play("superweapon_charge"); break;
            }
        }
    }

    private static bool Live(IReadOnlyDictionary<int, Node3D> actors, int id, out Node3D node)
    {
        node = null;
        return actors.TryGetValue(id, out node) && node != null && IsInstanceValid(node);
    }

    private MeshInstance3D SpawnMesh(Mesh mesh, Material mat, Vector3 pos)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mi);
        mi.GlobalPosition = pos;
        return mi;
    }

    /// <summary>Tween the node's per-instance transparency to fully faded, then free it.</summary>
    private static Tween FadeAndFree(GeometryInstance3D n, float seconds)
    {
        var tw = n.CreateTween();
        tw.TweenProperty(n, "transparency", 1.0f, seconds);
        tw.TweenCallback(Callable.From(n.QueueFree));
        return tw;
    }

    // ---- Fired: muzzle flash + tracer + shot sound ----

    private void OnFired(GameEvent ev, IReadOnlyDictionary<int, Node3D> actors, AudioDirector audio)
    {
        if (!Live(actors, ev.A, out var attacker)) return;
        Vector3 from = attacker.GlobalPosition + new Vector3(0, 0.4f, 0);

        // Muzzle flash: emissive billboard quad plus a brief omni flicker.
        var flash = SpawnMesh(MuzzleMesh, MuzzleMat, from);
        var light = new OmniLight3D { LightColor = Orange, LightEnergy = 3.0f, OmniRange = 3.0f, ShadowEnabled = false };
        flash.AddChild(light);
        var tw = FadeAndFree(flash, 0.08f);
        tw.Parallel().TweenProperty(light, "light_energy", 0.0f, 0.08f);

        if (Live(actors, ev.B, out var target))
        {
            Vector3 to = target.GlobalPosition + new Vector3(0, 0.4f, 0);
            Vector3 dir = to - from;
            float len = dir.Length();
            if (len > 0.05f)
            {
                var tracer = SpawnMesh(TracerMesh, TracerMat, (from + to) * 0.5f);
                // Guard the degenerate LookAt case where the shot is near vertical.
                Vector3 up = Mathf.Abs(dir.Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
                tracer.LookAt(to, up);
                tracer.Scale = new Vector3(0.05f, 0.05f, len);
                FadeAndFree(tracer, 0.12f);
            }
        }

        // The event does not carry a weapon type yet, so alternate rifle and
        // cannon by attacker id parity as an acceptable audio placeholder.
        audio?.PlayAt((ev.A & 1) == 0 ? "shot_rifle" : "shot_cannon", from);
    }

    // ---- Died: expanding flash + smoke puff + explosion sound ----

    private void OnDied(GameEvent ev, IReadOnlyDictionary<int, Node3D> actors, AudioDirector audio)
    {
        if (!Live(actors, ev.A, out var node)) return;
        Vector3 pos = node.GlobalPosition;

        // Expanding emissive sphere; the shared material stays orange and the
        // orange-to-dark read comes from the alpha fade against the ground.
        var flash = SpawnMesh(FlashMesh, FlashMat, pos + new Vector3(0, 0.3f, 0));
        flash.Scale = Vector3.One * 0.4f;
        FadeAndFree(flash, 0.3f)
            .Parallel().TweenProperty(flash, "scale", Vector3.One * 1.9f, 0.3f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);

        // One-shot smoke puff, freed shortly after its particles expire.
        var smoke = new GpuParticles3D
        {
            Amount = 12,
            Lifetime = 1.2f,
            OneShot = true,
            Explosiveness = 0.9f,
            ProcessMaterial = SmokeProcess,
            DrawPass1 = SmokeQuad,
            Emitting = true,
        };
        AddChild(smoke);
        smoke.GlobalPosition = pos + new Vector3(0, 0.3f, 0);
        var stw = smoke.CreateTween();
        stw.TweenInterval(1.5f);
        stw.TweenCallback(Callable.From(smoke.QueueFree));

        var scale = node.Scale;
        string name = node.Name.ToString().ToLowerInvariant();
        bool big = Mathf.Max(scale.X, Mathf.Max(scale.Y, scale.Z)) > 1.2f
            || name.Contains("yard") || name.Contains("factory") || name.Contains("refinery");
        audio?.PlayAt(big ? "explosion_large" : "explosion_small", pos);
    }

    // ---- SuperweaponImpact: shockwave ring + emissive column + light ----

    private void OnSuperweaponImpact(GameEvent ev, AudioDirector audio)
    {
        var pos = new Vector3(ToF(ev.X), 0, ToF(ev.Y));

        var ring = SpawnMesh(RingMesh, RingMat, pos + new Vector3(0, 0.15f, 0));
        ring.Scale = Vector3.One * 0.5f;
        FadeAndFree(ring, 1.2f)
            .Parallel().TweenProperty(ring, "scale", new Vector3(14.0f, 0.6f, 14.0f), 1.2f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);

        var column = SpawnMesh(ColumnMesh, ColumnMat, pos + new Vector3(0, 6.0f, 0));
        FadeAndFree(column, 0.9f)
            .Parallel().TweenProperty(column, "scale", new Vector3(0.2f, 1.15f, 0.2f), 0.9f);

        var light = new OmniLight3D { LightColor = Orange, LightEnergy = 6.0f, OmniRange = 18.0f, ShadowEnabled = false };
        AddChild(light);
        light.GlobalPosition = pos + new Vector3(0, 3.0f, 0);
        var ltw = light.CreateTween();
        ltw.TweenProperty(light, "light_energy", 0.0f, 0.5f);
        ltw.TweenCallback(Callable.From(light.QueueFree));

        audio?.PlayAt("superweapon_impact", pos);
    }
}
