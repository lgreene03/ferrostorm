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
        // Wave 1 of doc 20: storm sky, AgX grading, SSAO/SSIL grounding,
        // SSR for the water, volumetric fog the lights can play in, and a
        // glow curve tuned for tight emissive halos rather than screen wash.
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color(0.015f, 0.02f, 0.032f),
                    SkyHorizonColor = new Color(0.10f, 0.085f, 0.075f),
                    SkyCurve = 0.12f,
                    SkyEnergyMultiplier = 1.0f,
                    GroundBottomColor = new Color(0.01f, 0.012f, 0.016f),
                    GroundHorizonColor = new Color(0.09f, 0.075f, 0.065f),
                    GroundCurve = 0.14f,
                    SunAngleMax = 30f,
                    SunCurve = 0.15f,
                },
            },
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.34f, 0.38f, 0.46f),
            AmbientLightEnergy = 0.4f,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.35f,
            AdjustmentEnabled = true,
            AdjustmentBrightness = 1.03f,
            AdjustmentContrast = 1.14f,
            AdjustmentSaturation = 1.12f,
            SsaoEnabled = true,
            SsaoRadius = 1.5f,
            SsaoIntensity = 2.5f,
            SsaoPower = 1.8f,
            SsaoDetail = 0.6f,
            SsaoHorizon = 0.06f,
            SsaoSharpness = 0.98f,
            SsaoLightAffect = 0.1f,
            SsilEnabled = true,
            SsilRadius = 3.0f,
            SsilIntensity = 1.2f,
            SsilSharpness = 0.98f,
            SsilNormalRejection = 1.0f,
            SsrEnabled = true,
            SsrMaxSteps = 56,
            SsrFadeIn = 0.15f,
            SsrFadeOut = 2.0f,
            SsrDepthTolerance = 0.2f,
            GlowEnabled = true,
            GlowIntensity = 0.8f,
            GlowBloom = 0.0f,
            GlowStrength = 1.05f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,
            VolumetricFogEnabled = true,
            VolumetricFogDensity = 0.022f,
            VolumetricFogAlbedo = new Color(0.55f, 0.62f, 0.75f),
            VolumetricFogAnisotropy = 0.35f,
            VolumetricFogLength = 110f,
            VolumetricFogDetailSpread = 2.5f,
            VolumetricFogAmbientInject = 0.08f,
            VolumetricFogSkyAffect = 0.1f,
            VolumetricFogTemporalReprojectionEnabled = true,
        };
        env.SetGlowLevel(1, 0f);
        env.SetGlowLevel(2, 0.6f);
        env.SetGlowLevel(3, 1.0f);
        env.SetGlowLevel(4, 0.7f);
        env.SetGlowLevel(5, 0.35f);
        parent.AddChild(new WorldEnvironment { Environment = env, Name = "Env" });
    }

    /// <summary>Key/fill/rim rig (W1-01): warm key with quality shadows,
    /// cool sky fill, cool rim from the north so silhouettes read against
    /// the dark ground. Shared by the live scene and the replay theatre.</summary>
    public static void BuildLightRig(Node3D parent)
    {
        parent.AddChild(new DirectionalLight3D
        {
            Name = "KeySun",
            RotationDegrees = new Vector3(-48, 32, 0),
            LightColor = new Color(1.0f, 0.93f, 0.82f),
            LightEnergy = 1.7f,
            ShadowEnabled = true,
            ShadowBias = 0.03f,
            ShadowNormalBias = 1.2f,
            ShadowBlur = 1.5f,
            LightAngularDistance = 0.75f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowSplit1 = 0.22f,
            DirectionalShadowBlendSplits = true,
            DirectionalShadowFadeStart = 0.85f,
            DirectionalShadowMaxDistance = 90f,
        });
        parent.AddChild(new DirectionalLight3D
        {
            Name = "FillSky",
            RotationDegrees = new Vector3(-28, 148, 0),
            LightColor = new Color(0.55f, 0.65f, 0.85f),
            LightEnergy = 0.5f,
            ShadowEnabled = false,
        });
        parent.AddChild(new DirectionalLight3D
        {
            Name = "RimNorth",
            RotationDegrees = new Vector3(-18, 168, 0),
            LightColor = new Color(0.75f, 0.85f, 1.0f),
            LightEnergy = 0.6f,
            ShadowEnabled = false,
        });
    }

    // W1-06: the water scrolls; W1-11: shared contact-shadow blob texture.
    private static StandardMaterial3D? _waterMat;
    private static GradientTexture2D? _blobTex;

    public static void TickWater(double delta)
    {
        if (_waterMat != null)
            _waterMat.Uv1Offset += new Vector3(0.010f, 0.014f, 0f) * (float)delta;
    }

    private static GradientTexture2D BlobTex()
    {
        if (_blobTex != null) return _blobTex;
        var g = new Gradient();
        g.SetColor(0, new Color(0, 0, 0, 0.65f));
        g.SetColor(1, new Color(0, 0, 0, 0f));
        g.AddPoint(0.55f, new Color(0, 0, 0, 0.45f));
        _blobTex = new GradientTexture2D
        {
            Gradient = g,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(0.5f, 0f),
            Width = 128, Height = 128,
        };
        return _blobTex;
    }

    /// <summary>Fake contact shadow (W1-11): a radial dark decal under the
    /// actor. The thin Y extent keeps it off hull roofs.</summary>
    public static void AddContactBlob(Node3D node, float d)
    {
        node.AddChild(new Decal
        {
            TextureAlbedo = BlobTex(),
            Size = new Vector3(d, 0.5f, d),
            Position = new Vector3(0, -0.1f, 0),
            Name = "Blob",
        });
    }

    /// <summary>Ambient wind-blown dust motes (W1-12): one camera-following
    /// particle box that makes the air read as weather.</summary>
    public static GpuParticles3D BuildDust()
    {
        var quad = new QuadMesh { Size = new Vector2(0.05f, 0.05f) };
        quad.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            AlbedoColor = new Color(0.55f, 0.52f, 0.46f, 0.35f),
        };
        return new GpuParticles3D
        {
            Amount = 120,
            Lifetime = 7.0f,
            Preprocess = 4.0f,
            Emitting = true,
            DrawPass1 = quad,
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(28f, 6f, 28f),
                Direction = new Vector3(1f, 0f, 0.3f),
                Spread = 20.0f,
                InitialVelocityMin = 0.3f,
                InitialVelocityMax = 0.8f,
                Gravity = new Vector3(0f, -0.02f, 0f),
                ScaleMin = 0.6f,
                ScaleMax = 1.4f,
                TurbulenceEnabled = true,
                TurbulenceNoiseStrength = 0.6f,
                TurbulenceNoiseScale = 0.9f,
                TurbulenceInfluenceMin = 0.05f,
                TurbulenceInfluenceMax = 0.15f,
            },
        };
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
        // Shared dressing materials. Water is OPAQUE (W1-06): SSR skips
        // transparent geometry, and the slab sits below the ground lip so
        // opacity changes no silhouette. The static field lets TickWater
        // scroll the normals.
        _waterMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.03f, 0.055f, 0.085f),
            Metallic = 0.85f, Roughness = 0.08f,
            MetallicSpecular = 0.7f,
            NormalEnabled = true,
            NormalTexture = GrainTex(53, 0.08f, normalMap: true),
            NormalScale = 0.35f,
            Uv1Scale = new Vector3(6, 6, 6),
        };
        var waterMat = _waterMat;
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

    /// <summary>Selection ring, addable on demand (structures get one the
    /// first time they are selected).</summary>
    public static void AddSelRing(Node3D node, float radius)
    {
        var sel = new Color(0.95f, 0.90f, 0.70f);
        node.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = radius - 0.06f, OuterRadius = radius },
            Position = new Vector3(0, 0.03f, 0),
            Visible = true,
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

    /// <summary>Team-colour ground ring for a mobile unit plus a hidden
    /// selection ring the scene toggles. The one-place team-colour law,
    /// applied at the presentation layer.</summary>
    public static void DressMobile(Node3D node, int player, float blobDiameter = 1.15f)
    {
        AddContactBlob(node, blobDiameter);
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
