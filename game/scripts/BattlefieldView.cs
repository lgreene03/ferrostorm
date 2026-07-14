using Godot;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// Shared battlefield presentation: environment (tonemap/glow/fog), terrain
/// (cinder ground, Spine ridges, rubble) and per-actor dressing (team rings,
/// selection rings). Used by both ReplayTheater (replay playback) and
/// SkirmishLive (live sim). Presentation only - System.Random is fine here.
/// </summary>
public static class BattlefieldView
{
    public static readonly Color DirectorateMark = new(0.91f, 0.42f, 0.13f);
    public static readonly Color SodalityMark = new(0.24f, 0.70f, 0.63f);

    public static void BuildEnvironment(Node3D parent)
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.028f, 0.036f, 0.050f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.36f, 0.40f, 0.47f),
            AmbientLightEnergy = 0.7f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.15f,
            GlowEnabled = true,
            GlowIntensity = 0.4f,
            GlowBloom = 0.03f,
            FogEnabled = true,
            FogLightColor = new Color(0.045f, 0.055f, 0.075f),
            FogDensity = 0.0035f,
            FogSkyAffect = 0.45f,
        };
        parent.AddChild(new WorldEnvironment { Environment = env, Name = "Env" });
    }

    public static NoiseTexture2D GrainTex(int seed, float freq, bool normalMap = false,
        Color? dark = null, Color? light = null)
    {
        var noise = new FastNoiseLite
        {
            Seed = seed,
            Frequency = freq,
            FractalOctaves = 4,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        };
        var tex = new NoiseTexture2D
        {
            Noise = noise, Width = 512, Height = 512,
            Seamless = true, AsNormalMap = normalMap,
            BumpStrength = normalMap ? 6f : 8f,
        };
        if (!normalMap)
        {
            // Raw noise spans black..white - far too harsh for terrain. A
            // colour ramp pins the palette to a narrow band of the world's
            // materials instead.
            var grad = new Gradient();
            grad.SetColor(0, dark ?? new Color(0.10f, 0.105f, 0.12f));
            grad.SetColor(1, light ?? new Color(0.17f, 0.18f, 0.20f));
            tex.ColorRamp = grad;
        }
        return tex;
    }

    public static void BuildTerrain(Node3D parent, int w, int h, IEnumerable<(int X, int Y)> blockedCells)
    {
        var groundMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(7, 0.012f,
                dark: new Color(0.085f, 0.09f, 0.10f),
                light: new Color(0.145f, 0.15f, 0.165f)),
            NormalEnabled = true,
            NormalTexture = GrainTex(11, 0.05f, normalMap: true),
            NormalScale = 0.55f,
            Roughness = 0.96f,
            Uv1Scale = new Vector3(10, 10, 10),
        };
        // Exactly the map bounds: anything beyond is void, so the shroud
        // plane (same bounds) covers every lit surface and the map edge
        // reads as a classic hard boundary.
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(w, h) },
            Position = new Vector3(w / 2f, 0, h / 2f),
            MaterialOverride = groundMat,
            Name = "Ground",
        });

        var ridgeMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(23, 0.03f,
                dark: new Color(0.15f, 0.165f, 0.19f),
                light: new Color(0.26f, 0.28f, 0.31f)),
            NormalEnabled = true,
            NormalTexture = GrainTex(29, 0.09f, normalMap: true),
            NormalScale = 1.0f,
            Roughness = 0.92f,
            Uv1Scale = new Vector3(3, 3, 3),
        };
        var rng = new System.Random(2026);
        var blocked = new HashSet<(int, int)>();
        foreach (var (bx, by) in blockedCells)
        {
            blocked.Add((bx, by));
            float jh = 0.75f + (float)rng.NextDouble() * 0.55f;
            parent.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.18f, jh, 1.18f) },
                Position = new Vector3(bx + 0.5f, jh / 2f - 0.06f, by + 0.5f),
                Rotation = new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * 0.10f,
                    ((float)rng.NextDouble() - 0.5f) * 0.5f,
                    ((float)rng.NextDouble() - 0.5f) * 0.10f),
                MaterialOverride = ridgeMat,
            });
        }

        var rubbleMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(37, 0.05f,
                dark: new Color(0.13f, 0.14f, 0.16f),
                light: new Color(0.22f, 0.235f, 0.26f)),
            Roughness = 0.95f,
        };
        for (int i = 0; i < 220; i++)
        {
            float rx = (float)rng.NextDouble() * w, rz = (float)rng.NextDouble() * h;
            if (blocked.Contains(((int)rx, (int)rz))) continue;
            float s = 0.05f + (float)rng.NextDouble() * 0.16f;
            parent.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(s * (1f + (float)rng.NextDouble()), s * 0.7f, s) },
                Position = new Vector3(rx, s * 0.2f, rz),
                Rotation = new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * 0.6f,
                    (float)rng.NextDouble() * Mathf.Tau,
                    ((float)rng.NextDouble() - 0.5f) * 0.6f),
                MaterialOverride = rubbleMat,
            });
        }
    }

    /// <summary>Team-colour ground ring for a mobile unit plus a hidden
    /// selection ring the scene toggles. The one-place team-colour law,
    /// applied at the presentation layer.</summary>
    public static void DressMobile(Node3D node, int player)
    {
        var mark = player == 0 ? DirectorateMark : SodalityMark;
        node.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.30f, OuterRadius = 0.36f },
            Position = new Vector3(0, 0.02f, 0),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = mark,
                EmissionEnabled = true,
                Emission = mark,
                EmissionEnergyMultiplier = 0.9f,
            },
            Name = "TeamRing",
        });
        var sel = new Color(0.95f, 0.90f, 0.70f);
        node.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.42f, OuterRadius = 0.47f },
            Position = new Vector3(0, 0.03f, 0),
            Visible = false,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = sel,
                EmissionEnabled = true,
                Emission = sel,
                EmissionEnergyMultiplier = 1.4f,
            },
            Name = "SelRing",
        });
    }
}
