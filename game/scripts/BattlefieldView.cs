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

    /// <summary>Terrain with the full visual vocabulary (TICKET-P4-TER-01):
    /// cells the sim knows only as open or blocked render as what the map
    /// says they are - water, hills, ruins, fences, bridges.</summary>
    public static void BuildTerrain(Node3D parent, int w, int h,
        IEnumerable<(int X, int Y)> blockedCells,
        IReadOnlyDictionary<(int, int), char>? visual = null)
    {
        visual ??= new Dictionary<(int, int), char>();
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
        // Shared dressing materials
        var waterMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.09f, 0.13f, 0.92f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.6f, Roughness = 0.12f,
            NormalEnabled = true,
            NormalTexture = GrainTex(53, 0.08f, normalMap: true),
            NormalScale = 0.5f,
            Uv1Scale = new Vector3(6, 6, 6),
        };
        var hillMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(59, 0.04f,
                dark: new Color(0.11f, 0.115f, 0.125f),
                light: new Color(0.19f, 0.20f, 0.215f)),
            NormalEnabled = true,
            NormalTexture = GrainTex(61, 0.07f, normalMap: true),
            Roughness = 0.95f,
        };
        var ruinMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(67, 0.06f,
                dark: new Color(0.16f, 0.155f, 0.145f),
                light: new Color(0.28f, 0.27f, 0.25f)),
            Roughness = 0.9f,
        };
        var fenceMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.23f, 0.20f, 0.16f),
            Roughness = 0.85f,
        };
        var deckMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(71, 0.05f,
                dark: new Color(0.17f, 0.16f, 0.14f),
                light: new Color(0.27f, 0.25f, 0.22f)),
            Roughness = 0.88f,
        };

        var rng = new System.Random(2026);
        var blocked = new HashSet<(int, int)>();
        foreach (var (bx, by) in blockedCells)
        {
            blocked.Add((bx, by));
            char kind = visual.GetValueOrDefault((bx, by), '#');
            switch (kind)
            {
                case 'w':
                    // Water: a sunken reflective slab; the shore lip is the
                    // ground plane edge reading against it.
                    parent.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(1.02f, 0.1f, 1.02f) },
                        Position = new Vector3(bx + 0.5f, -0.12f, by + 0.5f),
                        MaterialOverride = waterMat,
                    });
                    break;
                case 'h':
                    // Hill: a broad smooth mound (sphere squashed flat), so
                    // clusters merge into rolling high ground.
                    float hr = 1.05f + (float)rng.NextDouble() * 0.25f;
                    parent.AddChild(new MeshInstance3D
                    {
                        Mesh = new SphereMesh { Radius = hr, Height = 1.5f + (float)rng.NextDouble() * 0.7f, RadialSegments = 14, Rings = 7 },
                        Position = new Vector3(bx + 0.5f, -0.15f, by + 0.5f),
                        MaterialOverride = hillMat,
                    });
                    break;
                case 'r':
                    // Ruin: broken wall stubs at jittered heights - a dead
                    // settlement to fight through.
                    float wh = 0.35f + (float)rng.NextDouble() * 0.5f;
                    parent.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(0.9f, wh, 0.9f) },
                        Position = new Vector3(bx + 0.5f, wh / 2f, by + 0.5f),
                        Rotation = new Vector3(0, ((float)rng.NextDouble() - 0.5f) * 0.3f, 0),
                        MaterialOverride = ruinMat,
                    });
                    if (rng.NextDouble() > 0.6)
                        parent.AddChild(new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(0.4f, wh * 1.6f, 0.3f) },
                            Position = new Vector3(bx + 0.3f + (float)rng.NextDouble() * 0.4f, wh * 0.8f, by + 0.3f + (float)rng.NextDouble() * 0.4f),
                            MaterialOverride = ruinMat,
                        });
                    break;
                case 'f':
                    // Fence: posts and two rails along the cell.
                    for (int px = 0; px < 2; px++)
                        parent.AddChild(new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.5f, 0.07f) },
                            Position = new Vector3(bx + 0.2f + px * 0.6f, 0.25f, by + 0.5f),
                            MaterialOverride = fenceMat,
                        });
                    for (int rail = 0; rail < 2; rail++)
                        parent.AddChild(new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(1.0f, 0.05f, 0.05f) },
                            Position = new Vector3(bx + 0.5f, 0.18f + rail * 0.22f, by + 0.5f),
                            MaterialOverride = fenceMat,
                        });
                    break;
                default:
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
                    break;
            }
        }

        // Bridges: OPEN cells with a raised deck and kerb rails - the sim
        // paths straight across; the eye reads a river crossing.
        foreach (var (cell, kind) in visual)
        {
            if (kind != 'B') continue;
            var (bx, by) = cell;
            parent.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.04f, 0.1f, 1.04f) },
                Position = new Vector3(bx + 0.5f, 0.05f, by + 0.5f),
                MaterialOverride = deckMat,
            });
            foreach (float off in new[] { -0.46f, 0.46f })
                parent.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.16f, 1.04f) },
                    Position = new Vector3(bx + 0.5f + off, 0.14f, by + 0.5f),
                    MaterialOverride = fenceMat,
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
