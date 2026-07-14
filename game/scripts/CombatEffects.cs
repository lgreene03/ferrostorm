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
///
/// Wave 3 (doc 20): per-weapon effect families keyed on the sim WeaponId
/// (hitscan tracers, travelling shells, arced rockets and howitzer rounds),
/// impact sparks and dirt, persistent scorch decals in a recycling ring
/// buffer, burning wrecks on big deaths, the full superweapon sequence, and
/// distance-scaled camera shake routed through the trauma pool on RtsCamera.
/// </summary>
public partial class CombatEffects : Node3D
{
    private static readonly Color Orange = new(1.0f, 0.55f, 0.15f);
    private static readonly Color Ember = new(1.0f, 0.35f, 0.08f);

    // Shared materials (static: never one resource per effect).
    private static readonly StandardMaterial3D MuzzleMat = Emissive(Orange, 3.0f, billboard: true);
    private static readonly StandardMaterial3D TracerMat = Emissive(new Color(1.0f, 0.8f, 0.4f), 2.5f);
    private static readonly StandardMaterial3D AutoTracerMat = Emissive(new Color(1.0f, 0.9f, 0.55f), 3.0f);
    private static readonly StandardMaterial3D FlashMat = Emissive(Ember, 2.0f);
    private static readonly StandardMaterial3D RingMat = Emissive(Orange, 2.0f);
    private static readonly StandardMaterial3D ColumnMat = Emissive(new Color(1.0f, 0.75f, 0.35f), 3.0f);
    private static readonly StandardMaterial3D ShellMat = Emissive(new Color(1.0f, 0.85f, 0.5f), 4.0f);
    private static readonly StandardMaterial3D RocketMat = Emissive(new Color(1.0f, 0.6f, 0.3f), 3.5f);
    private static readonly StandardMaterial3D HowShellMat = Emissive(new Color(1.0f, 0.75f, 0.4f), 3.0f);
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
    private static readonly CapsuleMesh ShellMesh = new() { Radius = 0.035f, Height = 0.22f };
    private static readonly BoxMesh RocketMesh = new() { Size = new Vector3(0.05f, 0.05f, 0.18f) };
    private static readonly SphereMesh HowShellMesh = new() { Radius = 0.06f, Height = 0.12f, RadialSegments = 8, Rings = 4 };
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

    // W3-03: rocket smoke trail (world-space puffs behind the moving round;
    // LocalCoords stays at its default false so the trail hangs in the air).
    private static readonly ParticleProcessMaterial RocketTrailProcess = new()
    {
        Direction = new Vector3(0, 0, 1),
        Spread = 10.0f,
        InitialVelocityMin = 0.2f,
        InitialVelocityMax = 0.5f,
        Gravity = new Vector3(0, 0.3f, 0),
        ScaleMin = 0.4f,
        ScaleMax = 0.8f,
        Color = new Color(0.35f, 0.34f, 0.32f, 0.6f),
    };

    // W3-04: howitzer dirt burst, also the ground-impact dirt kick (W3-05).
    private static readonly ParticleProcessMaterial DirtProcess = new()
    {
        Direction = new Vector3(0, 1, 0),
        Spread = 55.0f,
        InitialVelocityMin = 1.5f,
        InitialVelocityMax = 3.0f,
        Gravity = new Vector3(0, -6.0f, 0),
        ScaleMin = 0.5f,
        ScaleMax = 1.0f,
        Color = new Color(0.16f, 0.15f, 0.13f, 0.8f),
    };

    // W3-05: additive armour sparks fanning off a struck hull.
    private static readonly QuadMesh SparkQuad = new()
    {
        Size = new Vector2(0.08f, 0.08f),
        Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            AlbedoColor = new Color(1.0f, 0.85f, 0.45f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.85f, 0.45f),
            EmissionEnergyMultiplier = 4.0f,
        },
    };
    private static readonly ParticleProcessMaterial SparkProcess = new()
    {
        Direction = new Vector3(0, 1, 0),
        Spread = 60.0f,
        InitialVelocityMin = 2.5f,
        InitialVelocityMax = 5.0f,
        Gravity = new Vector3(0, -9.8f, 0),
        ScaleMin = 0.15f,
        ScaleMax = 0.3f,
        DampingMin = 1.0f,
        DampingMax = 2.0f,
    };

    // W3-06: one procedural scorch texture, one 48-slot recycling ring buffer,
    // zero art assets. Battle scars persist on explored ground by design.
    private static readonly GradientTexture2D ScorchTex = MakeScorchTex();
    private readonly Decal[] _scorch = new Decal[48];
    private int _scorchNext;

    // W3-07: burning wreck sites (fire core + smoke column + warm light).
    private static readonly StandardMaterial3D FireMat = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        VertexColorUseAsAlbedo = true,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
    };
    private static readonly QuadMesh FireQuad = MakeFireQuad();
    private static readonly ParticleProcessMaterial FireProcess = MakeFireProcess();
    private static readonly ParticleProcessMaterial SmokeColumnProcess = new()
    {
        Direction = new Vector3(0, 1, 0),
        Spread = 8.0f,
        InitialVelocityMin = 0.8f,
        InitialVelocityMax = 1.4f,
        Gravity = new Vector3(0, 0.4f, 0),
        ScaleMin = 0.8f,
        ScaleMax = 1.8f,
        Color = new Color(0.14f, 0.135f, 0.13f, 0.6f),
    };
    private readonly Queue<Node3D> _wrecks = new();

    // W3-08: damage-state smoke on sub-50% entities, two tiers, capped at 40.
    private static readonly ParticleProcessMaterial DmgSmokeProcess = new()
    {
        Direction = new Vector3(0, 1, 0),
        Spread = 12.0f,
        InitialVelocityMin = 0.4f,
        InitialVelocityMax = 0.8f,
        Gravity = new Vector3(0, 0.25f, 0),
        ScaleMin = 0.35f,
        ScaleMax = 0.7f,
        Color = new Color(0.18f, 0.17f, 0.16f, 0.55f),
    };
    private static readonly ParticleProcessMaterial DmgSmokeCritProcess = new()
    {
        Direction = new Vector3(0, 1, 0),
        Spread = 12.0f,
        InitialVelocityMin = 0.4f,
        InitialVelocityMax = 0.8f,
        Gravity = new Vector3(0, 0.25f, 0),
        ScaleMin = 0.35f,
        ScaleMax = 0.7f,
        Color = new Color(0.18f, 0.17f, 0.16f, 0.7f),
    };
    private static int _liveDmgSmoke;

    // W3-09: ferrite-gold motes rising off a loading harvester's intake.
    private static readonly QuadMesh HarvestQuad = MakeHarvestQuad();
    private static readonly ParticleProcessMaterial HarvestProcess = new()
    {
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
        EmissionBoxExtents = new Vector3(0.35f, 0.05f, 0.35f),
        Direction = new Vector3(0, 1, 0),
        Spread = 8.0f,
        InitialVelocityMin = 0.5f,
        InitialVelocityMax = 0.9f,
        Gravity = new Vector3(0, -0.4f, 0),
        ScaleMin = 0.2f,
        ScaleMax = 0.4f,
        Color = new Color(0.92f, 0.78f, 0.50f, 0.9f),
    };

    // W3-10: superweapon debris and dust pall shared resources.
    private static readonly QuadMesh DebrisQuad = MakeDebrisQuad();
    private static readonly ParticleProcessMaterial DebrisProcess = new()
    {
        Direction = new Vector3(0, 1, 0),
        Spread = 70.0f,
        InitialVelocityMin = 6.0f,
        InitialVelocityMax = 12.0f,
        Gravity = new Vector3(0, -9.8f, 0),
        ScaleMin = 0.6f,
        ScaleMax = 1.4f,
        AngularVelocityMin = -360f,
        AngularVelocityMax = 360f,
    };
    private static readonly ParticleProcessMaterial DustPallProcess = new()
    {
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
        EmissionSphereRadius = 3.0f,
        Direction = new Vector3(0, 1, 0),
        Spread = 40.0f,
        InitialVelocityMin = 0.3f,
        InitialVelocityMax = 0.8f,
        Gravity = new Vector3(0, 0.15f, 0),
        ScaleMin = 1.5f,
        ScaleMax = 3.0f,
        Color = new Color(0.2f, 0.19f, 0.17f, 0.5f),
    };
    private Node3D? _charge;

    /// <summary>W3-13: the live scene wires its RtsCamera here so effects can
    /// feed the trauma pool. ReplayTheater leaves it null.</summary>
    public RtsCamera? Camera;

    // W3-01: at most 8 muzzle omni lights alive at once; the billboard quad
    // always spawns so dense fights stay readable without a light storm.
    private int _liveMuzzleLights;

    private static QuadMesh MakeSmokeQuad()
    {
        var q = new QuadMesh { Size = new Vector2(0.5f, 0.5f) };
        q.Material = SmokeMat;
        return q;
    }

    private static QuadMesh MakeFireQuad()
    {
        var q = new QuadMesh { Size = new Vector2(0.4f, 0.4f) };
        q.Material = FireMat;
        return q;
    }

    private static ParticleProcessMaterial MakeFireProcess()
    {
        var fireGrad = new Gradient();
        fireGrad.SetColor(0, new Color(1.0f, 0.8f, 0.3f, 0.9f));
        fireGrad.SetColor(1, new Color(0.2f, 0.05f, 0.02f, 0.0f));
        fireGrad.AddPoint(0.5f, new Color(1.0f, 0.35f, 0.08f, 0.7f));
        return new ParticleProcessMaterial
        {
            Direction = new Vector3(0, 1, 0),
            Spread = 15.0f,
            InitialVelocityMin = 1.2f,
            InitialVelocityMax = 2.2f,
            Gravity = new Vector3(0, 0.8f, 0),
            ScaleMin = 0.5f,
            ScaleMax = 1.0f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.25f,
            ColorRamp = new GradientTexture1D { Gradient = fireGrad },
        };
    }

    private static GradientTexture2D MakeScorchTex()
    {
        var g = new Gradient();
        g.SetColor(0, new Color(0.02f, 0.02f, 0.02f, 0.9f));
        g.SetColor(1, new Color(0, 0, 0, 0));
        g.AddPoint(0.55f, new Color(0.05f, 0.045f, 0.04f, 0.55f));
        return new GradientTexture2D
        {
            Gradient = g,
            Width = 128,
            Height = 128,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(0.5f, 0.0f),
        };
    }

    private static QuadMesh MakeHarvestQuad()
    {
        var gold = new Color(0.92f, 0.78f, 0.50f);
        var q = new QuadMesh { Size = new Vector2(0.12f, 0.12f) };
        q.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            AlbedoColor = gold,
            EmissionEnabled = true,
            Emission = gold,
            EmissionEnergyMultiplier = 2.5f,
        };
        return q;
    }

    private static QuadMesh MakeDebrisQuad()
    {
        var q = new QuadMesh { Size = new Vector2(0.12f, 0.12f) };
        q.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            AlbedoColor = new Color(0.12f, 0.12f, 0.13f),
        };
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

    /// <summary>Consume one tick's worth of sim events and spawn effects.
    /// weaponOf resolves an attacker entity id to its sim WeaponId so each
    /// weapon class gets its own effect family (W3-01).</summary>
    public void OnTickEvents(IReadOnlyList<GameEvent> events, IReadOnlyDictionary<int, Node3D> actors,
        AudioDirector audio, System.Func<int, int>? weaponOf = null)
    {
        if (events == null || actors == null) return;
        foreach (var ev in events)
        {
            switch (ev.Type)
            {
                case GameEventType.Fired: OnFired(ev, actors, audio, weaponOf); break;
                case GameEventType.Died: OnDied(ev, actors, audio); break;
                case GameEventType.SuperweaponImpact: OnSuperweaponImpact(ev, audio); break;
                case GameEventType.ProductionComplete: audio?.Play("production_done", -6.0f); break;
                case GameEventType.SuperweaponLaunched: OnSuperweaponLaunched(ev, actors, audio); break;
            }
        }
    }

    private static bool Live(IReadOnlyDictionary<int, Node3D> actors, int id, out Node3D node)
    {
        node = null!;
        return actors.TryGetValue(id, out node!) && node != null && IsInstanceValid(node);
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

    /// <summary>One-shot particle emitter freed after its particles expire.</summary>
    private GpuParticles3D SpawnBurst(Mesh drawPass, ParticleProcessMaterial process, Vector3 pos,
        int amount, float lifetime, float freeAfter)
    {
        var p = new GpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            OneShot = true,
            Explosiveness = 1.0f,
            ProcessMaterial = process,
            DrawPass1 = drawPass,
            Emitting = true,
        };
        AddChild(p);
        p.GlobalPosition = pos;
        var tw = p.CreateTween();
        tw.TweenInterval(freeAfter);
        tw.TweenCallback(Callable.From(p.QueueFree));
        return p;
    }

    /// <summary>W3-13 / integration rule 1: every effect shake routes through
    /// here; trauma falls off linearly with distance from the camera's ground
    /// focus so far-off battles never rattle the player's view.</summary>
    public void Shake(float amount, Vector3 at, float falloff = 28f)
    {
        if (Camera == null) return;
        var focus = new Vector2(Camera.Position.X, Camera.Position.Z - Camera.Position.Y * 0.55f);
        float dist = focus.DistanceTo(new Vector2(at.X, at.Z));
        Camera.AddTrauma(amount * Mathf.Clamp(1f - dist / falloff, 0f, 1f));
    }

    // ---- Fired: per-weapon muzzle + projectile families (W3-01..W3-05) ----

    private void OnFired(GameEvent ev, IReadOnlyDictionary<int, Node3D> actors, AudioDirector? audio,
        System.Func<int, int>? weaponOf)
    {
        if (!Live(actors, ev.A, out var attacker)) return;
        Vector3 from = attacker.GlobalPosition + new Vector3(0, 0.4f, 0);
        int w = weaponOf?.Invoke(ev.A) ?? 0;

        // Muzzle family: quad size and light energy per weapon class. The
        // shared quad is 0.5 so per-class sizes come from instance scale.
        float quadSize = w switch { 2 => 0.4f, 7 => 0.3f, 1 or 4 or 6 => 0.7f, _ => 0.5f };
        float lightEnergy = w switch { 2 => 2.0f, 1 or 4 or 6 => 4.0f, _ => 3.0f };
        SpawnMuzzle(from, quadSize, lightEnergy);
        if (w is 1 or 4 or 6)
            SpawnBurst(SmokeQuad, SmokeProcess, from, amount: 6, lifetime: 0.8f, freeAfter: 1.0f);

        if (Live(actors, ev.B, out var target))
        {
            Vector3 to = target.GlobalPosition + new Vector3(0, 0.4f, 0);
            switch (w)
            {
                case 2: // TestRifle: thin hitscan tracer, instant impact
                    SpawnTracer(from, to, TracerMat, 0.04f, 0.10f);
                    SpawnImpact(to, armour: true);
                    HitPop(target);
                    break;
                case 7: // VanguardGun autocannon: 3-round burst
                    SpawnAutocannonBurst(actors, ev.A, ev.B);
                    break;
                case 1 or 4 or 6: // cannons: travelling shell
                    SpawnShell(actors, ev.B, from, to);
                    break;
                case 5: // Howitzer: high arc + splash-scaled dirt burst
                    SpawnHowitzerShell(actors, ev.B, from, to);
                    break;
                case 3: // TestRocket: arced rocket with smoke trail
                    SpawnRocket(actors, ev.B, from, to);
                    break;
                default: // unknown weapon: the original generic tracer
                    SpawnTracer(from, to, TracerMat, 0.05f, 0.12f);
                    break;
            }
        }

        audio?.PlayAt(w == 2 || w == 7 ? "shot_rifle" : "shot_cannon", from);
    }

    private void SpawnMuzzle(Vector3 from, float quadSize, float lightEnergy)
    {
        var flash = SpawnMesh(MuzzleMesh, MuzzleMat, from);
        flash.Scale = Vector3.One * (quadSize / 0.5f);
        var tw = flash.CreateTween();
        tw.TweenProperty(flash, "transparency", 1.0f, 0.08f);
        if (_liveMuzzleLights < 8)
        {
            _liveMuzzleLights++;
            var light = new OmniLight3D { LightColor = Orange, LightEnergy = lightEnergy, OmniRange = 3.0f, ShadowEnabled = false };
            flash.AddChild(light);
            tw.Parallel().TweenProperty(light, "light_energy", 0.0f, 0.08f);
            tw.Finished += () => _liveMuzzleLights--;
        }
        tw.TweenCallback(Callable.From(flash.QueueFree));
    }

    private void SpawnTracer(Vector3 from, Vector3 to, Material mat, float thickness, float fade)
    {
        Vector3 dir = to - from;
        float len = dir.Length();
        if (len <= 0.05f) return;
        var tracer = SpawnMesh(TracerMesh, mat, (from + to) * 0.5f);
        // Guard the degenerate LookAt case where the shot is near vertical.
        Vector3 up = Mathf.Abs(dir.Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
        tracer.LookAt(to, up);
        tracer.Scale = new Vector3(thickness, thickness, len);
        FadeAndFree(tracer, fade);
    }

    private void SpawnAutocannonBurst(IReadOnlyDictionary<int, Node3D> actors, int attackerId, int targetId)
    {
        void Round()
        {
            if (!Live(actors, attackerId, out var a) || !Live(actors, targetId, out var t)) return;
            Vector3 f = a.GlobalPosition + new Vector3(0, 0.4f, 0);
            Vector3 to = t.GlobalPosition + new Vector3(0, 0.4f, 0);
            SpawnTracer(f, to, AutoTracerMat, 0.03f, 0.10f);
            SpawnImpact(to, armour: true);
            HitPop(t);
        }
        Round();
        GetTree().CreateTimer(0.05).Timeout += () => { if (IsInsideTree()) Round(); };
        GetTree().CreateTimer(0.10).Timeout += () => { if (IsInsideTree()) Round(); };
    }

    // W3-02: travelling cannon shell for weapons 1/4/6.
    private void SpawnShell(IReadOnlyDictionary<int, Node3D> actors, int targetId, Vector3 from, Vector3 to)
    {
        var shell = SpawnMesh(ShellMesh, ShellMat, from);
        Vector3 dir = to - from;
        if (dir.Length() > 0.05f)
        {
            Vector3 up = Mathf.Abs(dir.Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
            shell.LookAt(to, up);
            // CapsuleMesh's long axis is Y; rotating 90 degrees about local X
            // points it down -Z, the LookAt forward.
            shell.RotateObjectLocal(Vector3.Right, Mathf.Pi / 2);
        }
        float dur = Mathf.Max(0.08f, from.DistanceTo(to) / 28f);
        var tw = shell.CreateTween();
        tw.TweenProperty(shell, "global_position", to, dur);
        tw.TweenCallback(Callable.From(() =>
        {
            SpawnImpact(to, armour: true);
            if (Live(actors, targetId, out var t)) HitPop(t);
            shell.QueueFree();
        }));
    }

    // W3-03: rocket on a shallow quadratic bezier with a world-space trail.
    private void SpawnRocket(IReadOnlyDictionary<int, Node3D> actors, int targetId, Vector3 from, Vector3 to)
    {
        var proj = SpawnMesh(RocketMesh, RocketMat, from);
        Vector3 c = (from + to) * 0.5f + Vector3.Up * Mathf.Clamp(from.DistanceTo(to) * 0.25f, 0.4f, 1.5f);
        float dur = Mathf.Max(0.15f, from.DistanceTo(to) / 14f);
        var trail = new GpuParticles3D
        {
            Amount = 40,
            Lifetime = 0.6f,
            Emitting = true,
            DrawPass1 = SmokeQuad,
            ProcessMaterial = RocketTrailProcess,
        };
        proj.AddChild(trail);
        var tw = proj.CreateTween();
        tw.TweenMethod(Callable.From((float t) =>
        {
            proj.GlobalPosition = from * ((1 - t) * (1 - t)) + c * (2 * (1 - t) * t) + to * (t * t);
        }), 0.0f, 1.0f, dur);
        tw.TweenCallback(Callable.From(() =>
        {
            // Reparent keeps the global transform, so the last puffs hang in
            // the air behind the detonation instead of vanishing with it.
            trail.Reparent(this);
            trail.Emitting = false;
            var ttw = trail.CreateTween();
            ttw.TweenInterval(0.8f);
            ttw.TweenCallback(Callable.From(trail.QueueFree));
            SpawnImpact(to, armour: true);
            if (Live(actors, targetId, out var t)) HitPop(t);
            proj.QueueFree();
        }));
    }

    // W3-04: howitzer round on a high arc; the sim resolved the damage on its
    // own tick, so the 0.9s flight is pure presentation lag on the impact
    // visual - the classic RTS artillery read.
    private void SpawnHowitzerShell(IReadOnlyDictionary<int, Node3D> actors, int targetId, Vector3 from, Vector3 to)
    {
        var shell = SpawnMesh(HowShellMesh, HowShellMat, from);
        Vector3 c = (from + to) * 0.5f + Vector3.Up * Mathf.Clamp(from.DistanceTo(to) * 0.6f, 2.0f, 6.0f);
        const float dur = 0.9f;
        var tw = shell.CreateTween();
        tw.TweenMethod(Callable.From((float t) =>
        {
            shell.GlobalPosition = from * ((1 - t) * (1 - t)) + c * (2 * (1 - t) * t) + to * (t * t);
        }), 0.0f, 1.0f, dur);
        tw.TweenCallback(Callable.From(() =>
        {
            SpawnDirt(to, 24);
            var flash = SpawnMesh(FlashMesh, FlashMat, to);
            flash.Scale = Vector3.One * 0.35f;
            FadeAndFree(flash, 0.25f)
                .Parallel().TweenProperty(flash, "scale", Vector3.One * 1.0f, 0.25f);
            // Splash-radius ring matching the sim's 1.5-cell splash so
            // players read the area of effect.
            var ring = SpawnMesh(RingMesh, RingMat, to + new Vector3(0, 0.1f, 0));
            ring.Scale = Vector3.One * 0.3f;
            var rtw = ring.CreateTween();
            rtw.TweenProperty(ring, "scale", new Vector3(1.5f, 0.3f, 1.5f), 0.4f);
            rtw.TweenProperty(ring, "transparency", 1.0f, 0.3f);
            rtw.TweenCallback(Callable.From(ring.QueueFree));
            AddScorch(to, 1.6f);
            Shake(0.15f, to);
            if (Live(actors, targetId, out var t)) HitPop(t);
            shell.QueueFree();
        }));
    }

    // ---- W3-05: impact family at the target ----

    /// <summary>Armour hit: additive sparks fan upward with a hot flash pop.
    /// Ground hit: a dirt kick reusing the howitzer's dirt material.</summary>
    private void SpawnImpact(Vector3 p, bool armour)
    {
        if (armour)
        {
            SpawnBurst(SparkQuad, SparkProcess, p, amount: 10, lifetime: 0.35f, freeAfter: 0.5f);
            var flash = SpawnMesh(FlashMesh, FlashMat, p);
            flash.Scale = Vector3.One * 0.2f;
            FadeAndFree(flash, 0.15f);
        }
        else
        {
            SpawnDirt(p, 14);
        }
    }

    private void SpawnDirt(Vector3 p, int amount)
        => SpawnBurst(SmokeQuad, DirtProcess, p, amount, lifetime: 0.7f, freeAfter: 1.0f);

    /// <summary>Tiny scale-pop on the struck actor. The meta flag prevents
    /// stacking; the scale guard skips actors mid rise/collapse animation so
    /// the pop never freezes a structure at a part-grown scale.</summary>
    private static void HitPop(Node3D tgt)
    {
        if (tgt.HasMeta("hitpop") || !tgt.Scale.IsEqualApprox(Vector3.One)) return;
        tgt.SetMeta("hitpop", true);
        var rest = tgt.Scale;
        var htw = tgt.CreateTween();
        htw.TweenProperty(tgt, "scale", rest * 1.06f, 0.04f);
        htw.TweenProperty(tgt, "scale", rest, 0.10f);
        htw.Finished += () => { if (IsInstanceValid(tgt)) tgt.RemoveMeta("hitpop"); };
    }

    // ---- W3-06: persistent scorch decals with a recycling cap ----

    /// <summary>Battle scars: a radial burn decal at pos, d units across. The
    /// 48-slot ring buffer recycles the oldest mark with a short fade so the
    /// battlefield keeps its history at a fixed cost.</summary>
    public void AddScorch(Vector3 pos, float d)
    {
        int idx = _scorchNext;
        _scorchNext = (_scorchNext + 1) % _scorch.Length;
        var slot = _scorch[idx];
        if (slot == null || !IsInstanceValid(slot))
        {
            slot = new Decal { TextureAlbedo = ScorchTex, AlbedoMix = 1.0f };
            AddChild(slot);
            _scorch[idx] = slot;
            PlaceScorch(slot, pos, d);
        }
        else
        {
            // Recycling: fade the old mark out, then reposition (no pop).
            var s = slot;
            var tw = s.CreateTween();
            tw.TweenProperty(s, "modulate:a", 0.0f, 0.3f);
            tw.TweenCallback(Callable.From(() => PlaceScorch(s, pos, d)));
        }
    }

    private static void PlaceScorch(Decal slot, Vector3 pos, float d)
    {
        // Decal projects along its local -Y; 0.8 depth catches ground undulation.
        slot.Size = new Vector3(d, 0.8f, d);
        slot.GlobalPosition = pos + new Vector3(0, 0.4f, 0);
        slot.RotationDegrees = new Vector3(0, (float)GD.RandRange(0, 360), 0);
        slot.Visible = true;
        slot.Modulate = Colors.White;
    }

    // ---- W3-07: burning wrecks on big deaths ----

    private void SpawnBurnSite(Vector3 pos)
    {
        var site = new Node3D();
        AddChild(site);
        site.GlobalPosition = pos;
        var fire = new GpuParticles3D
        {
            Amount = 16,
            Lifetime = 0.7f,
            Emitting = true,
            DrawPass1 = FireQuad,
            ProcessMaterial = FireProcess,
        };
        var smoke = new GpuParticles3D
        {
            Amount = 20,
            Lifetime = 2.5f,
            Emitting = true,
            DrawPass1 = SmokeQuad,
            ProcessMaterial = SmokeColumnProcess,
        };
        var light = new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.45f, 0.12f),
            LightEnergy = 1.2f,
            OmniRange = 4.0f,
            ShadowEnabled = false,
        };
        site.AddChild(fire);
        site.AddChild(smoke);
        site.AddChild(light);
        var tw = site.CreateTween();
        tw.TweenInterval(6.0);
        tw.TweenCallback(Callable.From(() => { fire.Emitting = false; smoke.Emitting = false; }));
        tw.TweenInterval(2.5); // let the last particles die
        tw.Parallel().TweenProperty(light, "light_energy", 0.0f, 2.0f);
        tw.TweenCallback(Callable.From(site.QueueFree));
        _wrecks.Enqueue(site);
        if (_wrecks.Count > 10)
        {
            var oldest = _wrecks.Dequeue();
            if (IsInstanceValid(oldest)) oldest.QueueFree();
        }
    }

    // ---- W3-08 / W3-09: emitter factories the live scene attaches ----

    /// <summary>Damage-state smoke for a sub-50% entity. Tier 2 (sub-25%) is
    /// denser, darker and faster. Returns null at the 40-emitter cap; the
    /// caller must null-check.</summary>
    public static GpuParticles3D? MakeDamageSmoke(int tier)
    {
        if (_liveDmgSmoke >= 40) return null;
        _liveDmgSmoke++;
        var p = new GpuParticles3D
        {
            Amount = tier >= 2 ? 14 : 8,
            Lifetime = 1.4f,
            Emitting = true,
            DrawPass1 = SmokeQuad,
            ProcessMaterial = tier >= 2 ? DmgSmokeCritProcess : DmgSmokeProcess,
            SpeedScale = tier >= 2 ? 1.6f : 1.0f,
        };
        p.SetMeta("tier", tier);
        p.TreeExited += () => _liveDmgSmoke--;
        return p;
    }

    /// <summary>Ferrite-gold particle stream for a loading harvester.</summary>
    public static GpuParticles3D MakeHarvestFx() => new()
    {
        Amount = 12,
        Lifetime = 0.8f,
        Emitting = true,
        DrawPass1 = HarvestQuad,
        ProcessMaterial = HarvestProcess,
    };

    // ---- Died: flash + smoke + scars + wrecks + shake (W3-06/07/13) ----

    private void OnDied(GameEvent ev, IReadOnlyDictionary<int, Node3D> actors, AudioDirector? audio)
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
        AddScorch(pos, big ? 2.2f : 1.2f);
        if (big) SpawnBurnSite(pos);
        Shake(big ? 0.30f : 0.12f, pos);
        audio?.PlayAt(big ? "explosion_large" : "explosion_small", pos);
    }

    // ---- W3-10: the superweapon sequence ----

    // Stage 1: a pulsing charge glow on the dish from launch until impact.
    private void OnSuperweaponLaunched(GameEvent ev, IReadOnlyDictionary<int, Node3D> actors, AudioDirector? audio)
    {
        audio?.Play("superweapon_charge");
        if (!Live(actors, ev.A, out var sw)) return;
        if (_charge != null && IsInstanceValid(_charge)) _charge.QueueFree();
        var charge = new Node3D { Name = "Charge" };
        AddChild(charge);
        charge.GlobalPosition = sw.GlobalPosition + new Vector3(0, 1.0f, 0);
        var orb = new MeshInstance3D
        {
            Mesh = FlashMesh,
            MaterialOverride = Emissive(new Color(1.0f, 0.6f, 0.2f), 5.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Scale = Vector3.One * 0.2f,
        };
        charge.AddChild(orb);
        var pulse = orb.CreateTween();
        pulse.SetLoops();
        pulse.TweenProperty(orb, "scale", Vector3.One * 0.45f, 0.25f);
        pulse.TweenProperty(orb, "scale", Vector3.One * 0.2f, 0.25f);
        var light = new OmniLight3D { LightColor = Orange, LightEnergy = 0.0f, OmniRange = 8.0f, ShadowEnabled = false };
        charge.AddChild(light);
        var ltw = light.CreateTween();
        ltw.TweenProperty(light, "light_energy", 3.0f, 2.0f);
        _charge = charge;
    }

    // Stage 2: sky beam, shockwave ring, column, flash, debris, dust pall,
    // a six-unit scorch and a distance-scaled shake. Stage timing and scale
    // are the Wave 5 taste-pass flag; every value here is the committed start.
    private void OnSuperweaponImpact(GameEvent ev, AudioDirector? audio)
    {
        var pos = new Vector3(ToF(ev.X), 0, ToF(ev.Y));

        if (_charge != null && IsInstanceValid(_charge)) _charge.QueueFree();
        _charge = null;

        // (a) Sky beam: a tall emissive cylinder flaring out as it fades.
        var beam = SpawnMesh(
            new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.35f, Height = 40f, RadialSegments = 12 },
            Emissive(new Color(1.0f, 0.9f, 0.7f), 6.0f),
            pos + new Vector3(0, 20f, 0));
        FadeAndFree(beam, 0.4f)
            .Parallel().TweenProperty(beam, "scale", new Vector3(2.5f, 1.0f, 2.5f), 0.4f);

        // (b) The original shockwave ring + column + light.
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

        // (c) Expanding flash sphere.
        var flash = SpawnMesh(FlashMesh, FlashMat, pos + new Vector3(0, 0.5f, 0));
        flash.Scale = Vector3.One * 0.5f;
        FadeAndFree(flash, 0.5f)
            .Parallel().TweenProperty(flash, "scale", Vector3.One * 4.0f, 0.5f);

        // (d) Debris burst: dark tumbling chips on hard ballistic arcs.
        SpawnBurst(DebrisQuad, DebrisProcess, pos, amount: 40, lifetime: 1.6f, freeAfter: 2.0f);

        // (e) Lingering dust pall over ground zero.
        var pall = new GpuParticles3D
        {
            Amount = 32,
            Lifetime = 6.0f,
            OneShot = true,
            Explosiveness = 0.3f,
            DrawPass1 = SmokeQuad,
            ProcessMaterial = DustPallProcess,
            Emitting = true,
        };
        AddChild(pall);
        pall.GlobalPosition = pos;
        var ptw = pall.CreateTween();
        ptw.TweenInterval(8.0f);
        ptw.TweenCallback(Callable.From(pall.QueueFree));

        // (f) The scar and the shake.
        AddScorch(pos, 6.0f);
        Shake(0.9f, pos, falloff: 60f);

        audio?.PlayAt("superweapon_impact", pos);
    }
}
