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

    // W4-10: heightfield undulation. GroundHeight is the single sampler the
    // ground mesh, scatter and actor placement all agree on; shores, bridge
    // approaches and water stay level via _flatCells. Amplitude 0.075 keeps
    // total undulation ~0.15 so units read grounded and the y=3.2 shroud
    // plane clears everything.
    private static FastNoiseLite? _groundNoise;
    private static HashSet<(int, int)>? _flatCells;
    private static HashSet<(int, int)>? _basinCells;

    public static float GroundHeight(float x, float z)
    {
        if (_groundNoise == null || _flatCells == null) return 0f;
        // Water cells: the ground sheet dives into a basin below the water
        // slab (slab bottom -0.17), otherwise the heightfield would cover
        // the slab (top -0.07) and the river would render as dust. Shore
        // cells stay level at 0, so the lip drops at the cell line and the
        // W4-14 ramps dress the transition. No actor ever stands here
        // (water is blocked; bridges are 'B' and flat).
        if (_basinCells != null && _basinCells.Contains(((int)x, (int)z)))
            return -0.22f;
        if (_flatCells.Contains(((int)x, (int)z)))
            return 0f;
        return _groundNoise.GetNoise2D(x, z) * 0.075f;
    }

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

    // W4-16: shared tyre-track decal texture - two broken tread bars.
    private static ImageTexture? _trackTex;

    public static ImageTexture TrackTex()
    {
        if (_trackTex != null) return _trackTex;
        var img = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        foreach (int x0 in new[] { 34, 84 })
            for (int x = x0; x <= x0 + 10; x++)
                for (int y = 0; y < 128; y++)
                {
                    // Knob iteration (recorded): spec alpha 0.38 vanished
                    // under the dusk AgX grade on the noisy splat ground -
                    // the 0.65-alpha contact blobs were the visible bound.
                    float alpha = 0.62f;
                    if ((y / 6) % 2 == 0) alpha *= 0.55f;
                    img.SetPixel(x, y, new Color(0.03f, 0.03f, 0.032f, alpha));
                }
        _trackTex = ImageTexture.CreateFromImage(img);
        return _trackTex;
    }

    // W4-17: the amber ground stain under ferrite deposits - doc 16 law,
    // ferrite gold is the light of this world.
    private static GradientTexture2D? _stainTex;

    public static GradientTexture2D FerriteStainTex()
    {
        if (_stainTex != null) return _stainTex;
        var g = new Gradient();
        g.SetColor(0, new Color(0.55f, 0.42f, 0.20f, 0.42f));
        g.SetColor(1, new Color(0, 0, 0, 0));
        g.AddPoint(0.6f, new Color(0.30f, 0.22f, 0.10f, 0.18f));
        _stainTex = new GradientTexture2D
        {
            Gradient = g,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(0.98f, 0.5f),
            Width = 128, Height = 128,
        };
        return _stainTex;
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

    /// <summary>W4-11: like GrainTex but raw greyscale (no colour ramp) -
    /// the splat shader's blend masks and per-layer grain.</summary>
    private static NoiseTexture2D RawNoise(int seed, float freq)
    {
        return new NoiseTexture2D
        {
            Noise = new FastNoiseLite
            {
                Seed = seed,
                Frequency = freq,
                FractalOctaves = 4,
                NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            },
            Width = 512, Height = 512,
            Seamless = true,
        };
    }

    /// <summary>Terrain with the full visual vocabulary (TICKET-P4-TER-01):
    /// cells the sim knows only as open or blocked render as what the map
    /// says they are - water, hills, ruins, fences, bridges.</summary>
    public static void BuildTerrain(Node3D parent, int w, int h,
        IEnumerable<(int X, int Y)> blockedCells,
        IReadOnlyDictionary<(int, int), char>? visual = null)
    {
        visual ??= new Dictionary<(int, int), char>();
        // W4-13: the full blocked set, hoisted before any loop so ridge
        // cells can classify their neighbours.
        var blockedSet = new HashSet<(int, int)>(blockedCells);
        // W4-14: water cells drive shore ramps and the flat-cell mask.
        var waterCells = new HashSet<(int, int)>();
        foreach (var (cell, kind) in visual)
            if (kind == 'w') waterCells.Add(cell);

        // W4-10: undulation noise plus the cells that must stay level -
        // shores (Chebyshev <= 1 of water) and bridge approaches.
        _groundNoise = new FastNoiseLite
        {
            Seed = 7134,
            Frequency = 0.045f,
            FractalOctaves = 3,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        };
        _flatCells = new HashSet<(int, int)>();
        _basinCells = waterCells;
        foreach (var (cell, kind) in visual)
        {
            var (cx, cy) = cell;
            if (kind == 'w')
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        _flatCells.Add((cx + dx, cy + dy));
            else if (kind == 'B')
                _flatCells.Add((cx, cy));
        }

        // W4-11: three-layer splat shader (dust/rock/gravel by world-space
        // noise masks) replaces the single colour-ramped noise material.
        var groundShader = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://shaders/ground_splat.gdshader"),
        };
        groundShader.SetShaderParameter("noise_a", RawNoise(101, 0.02f));
        groundShader.SetShaderParameter("noise_b", RawNoise(103, 0.02f));
        groundShader.SetShaderParameter("detail_normal", GrainTex(11, 0.05f, normalMap: true));

        // W4-10: heightfield ground - generated ArrayMesh at 2 verts per
        // cell, displaced by GroundHeight, normals by central difference.
        // Exactly the map bounds: anything beyond is void, so the shroud
        // plane (same bounds) covers every lit surface and the map edge
        // reads as a classic hard boundary.
        int vw = w * 2 + 1, vh = h * 2 + 1;
        var verts = new Vector3[vw * vh];
        var normals = new Vector3[vw * vh];
        var uvs = new Vector2[vw * vh];
        var tangents = new float[vw * vh * 4]; // +X tangent; NORMAL_MAP needs one
        var indices = new int[w * 2 * h * 2 * 6];
        const float e = 0.25f;
        for (int iz = 0; iz < vh; iz++)
            for (int ix = 0; ix < vw; ix++)
            {
                int i = iz * vw + ix;
                float x = ix * 0.5f, z = iz * 0.5f;
                verts[i] = new Vector3(x, GroundHeight(x, z), z);
                uvs[i] = new Vector2(x / w, z / h);
                normals[i] = new Vector3(
                    GroundHeight(x - e, z) - GroundHeight(x + e, z),
                    2f * e,
                    GroundHeight(x, z - e) - GroundHeight(x, z + e)).Normalized();
                tangents[i * 4] = 1f;
                tangents[i * 4 + 3] = 1f;
            }
        int idx = 0;
        for (int iz = 0; iz < vh - 1; iz++)
            for (int ix = 0; ix < vw - 1; ix++)
            {
                int i0 = iz * vw + ix, i1 = i0 + 1, i2 = i0 + vw, i3 = i2 + 1;
                // Godot front faces wind CLOCKWISE (PlaneMesh precedent), so
                // the +Y-facing winding is (i0,i1,i2)/(i1,i3,i2) - the spec's
                // documented swap of its first guess.
                indices[idx++] = i0; indices[idx++] = i1; indices[idx++] = i2;
                indices[idx++] = i1; indices[idx++] = i3; indices[idx++] = i2;
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Tangent] = tangents;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var ground = new ArrayMesh();
        ground.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        parent.AddChild(new MeshInstance3D
        {
            Mesh = ground,
            Position = Vector3.Zero, // verts already in world XZ
            MaterialOverride = groundShader,
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
            // W4-14 Part A: per-cell slabs share one continuous world-space
            // pattern, so the per-cell UV seam disappears.
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
        };
        var waterMat = _waterMat;
        // W4-14 Part B/C: wet shore ramps and the foam line at the lip.
        var shoreMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(89, 0.05f,
                dark: new Color(0.055f, 0.06f, 0.068f),
                light: new Color(0.10f, 0.105f, 0.115f)),
            Roughness = 0.60f, // darker and glossier: wet ground
        };
        var foamMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.19f, 0.21f, 0.45f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.4f,
        };
        // W4-13: strata material for cliff bases, declared once.
        var cliffBaseMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(31, 0.06f,
                dark: new Color(0.10f, 0.105f, 0.115f),
                light: new Color(0.17f, 0.18f, 0.20f)),
            NormalEnabled = true,
            NormalTexture = GrainTex(29, 0.09f, normalMap: true),
            NormalScale = 1.0f,
            Roughness = 0.94f,
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
        // W4-13: talus debris at ridge feet folds into the rubble MultiMesh
        // as extra instances rather than per-piece nodes.
        var talus = new List<(Transform3D Xform, Color Shade)>();
        foreach (var (bx, by) in blockedCells)
        {
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
                    // W4-14 Part B/C: on each edge that meets ground, a wet
                    // ramp descending from the ground lip to the waterline,
                    // and a faint foam line just inside the water cell.
                    // Ramp high edges sit at y~0 against the ground lip;
                    // rotation signs verified against the capture.
                    if (!waterCells.Contains((bx - 1, by)))
                    {
                        parent.AddChild(new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(0.36f, 0.02f, 1.04f) },
                            Position = new Vector3(bx + 0.17f, -0.05f, by + 0.5f),
                            Rotation = new Vector3(0, 0, -0.33f),
                            MaterialOverride = shoreMat,
                        });
                        parent.AddChild(new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.012f, 1.04f) },
                            Position = new Vector3(bx + 0.04f, -0.065f, by + 0.5f),
                            MaterialOverride = foamMat,
                        });
                    }
                    if (!waterCells.Contains((bx + 1, by)))
                    {
                        parent.AddChild(new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(0.36f, 0.02f, 1.04f) },
                            Position = new Vector3(bx + 0.83f, -0.05f, by + 0.5f),
                            Rotation = new Vector3(0, 0, 0.33f),
                            MaterialOverride = shoreMat,
                        });
                        parent.AddChild(new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.012f, 1.04f) },
                            Position = new Vector3(bx + 0.96f, -0.065f, by + 0.5f),
                            MaterialOverride = foamMat,
                        });
                    }
                    if (!waterCells.Contains((bx, by - 1)))
                    {
                        parent.AddChild(new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(1.04f, 0.02f, 0.36f) },
                            Position = new Vector3(bx + 0.5f, -0.05f, by + 0.17f),
                            Rotation = new Vector3(0.33f, 0, 0),
                            MaterialOverride = shoreMat,
                        });
                        parent.AddChild(new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(1.04f, 0.012f, 0.07f) },
                            Position = new Vector3(bx + 0.5f, -0.065f, by + 0.04f),
                            MaterialOverride = foamMat,
                        });
                    }
                    if (!waterCells.Contains((bx, by + 1)))
                    {
                        parent.AddChild(new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(1.04f, 0.02f, 0.36f) },
                            Position = new Vector3(bx + 0.5f, -0.05f, by + 0.83f),
                            Rotation = new Vector3(-0.33f, 0, 0),
                            MaterialOverride = shoreMat,
                        });
                        parent.AddChild(new MeshInstance3D
                        {
                            Mesh = new BoxMesh { Size = new Vector3(1.04f, 0.012f, 0.07f) },
                            Position = new Vector3(bx + 0.5f, -0.065f, by + 0.96f),
                            MaterialOverride = foamMat,
                        });
                    }
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
                    // W4-13: classify rim vs interior; per-cell deterministic
                    // rng so map edits do not reshuffle every ridge.
                    var crng = new System.Random(bx * 73856093 ^ by * 19349663);
                    bool boundary = false;
                    foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        if (!blockedSet.Contains((bx + dx, by + dy))) { boundary = true; break; }
                    // Interior massif reads taller behind the rim.
                    float jh = boundary
                        ? 0.75f + (float)crng.NextDouble() * 0.55f
                        : 0.95f + (float)crng.NextDouble() * 0.55f;
                    parent.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(1.18f, jh, 1.18f) },
                        Position = new Vector3(bx + 0.5f, jh / 2f - 0.06f, by + 0.5f),
                        Rotation = new Vector3(
                            ((float)crng.NextDouble() - 0.5f) * 0.10f,
                            ((float)crng.NextDouble() - 0.5f) * 0.5f,
                            ((float)crng.NextDouble() - 0.5f) * 0.10f),
                        MaterialOverride = ridgeMat,
                    });
                    if (!boundary) break;
                    // Boundary cells: stepped strata plus a dust-capped top.
                    parent.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(1.30f, jh * 0.38f, 1.30f) },
                        Position = new Vector3(bx + 0.5f, jh * 0.19f - 0.06f, by + 0.5f),
                        Rotation = new Vector3(0, ((float)crng.NextDouble() - 0.5f) * 0.12f, 0),
                        MaterialOverride = cliffBaseMat,
                    });
                    parent.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(1.22f, jh * 0.30f, 1.22f) },
                        Position = new Vector3(bx + 0.5f, jh * 0.55f - 0.06f, by + 0.5f),
                        Rotation = new Vector3(0, ((float)crng.NextDouble() - 0.5f) * 0.18f, 0),
                        MaterialOverride = ridgeMat,
                    });
                    parent.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(1.08f, 0.07f, 1.08f) },
                        Position = new Vector3(bx + 0.5f, jh - 0.04f, by + 0.5f),
                        MaterialOverride = groundShader, // dust-capped mesa read
                    });
                    // Talus skirt: small debris boxes at the ridge foot,
                    // folded into the rubble MultiMesh below.
                    int tn = 5 + crng.Next(5);
                    for (int ti = 0; ti < tn; ti++)
                    {
                        float ts = 0.05f + (float)crng.NextDouble() * 0.12f;
                        float px = bx + 0.5f + ((float)crng.NextDouble() - 0.5f) * 1.7f;
                        float pz = by + 0.5f + ((float)crng.NextDouble() - 0.5f) * 1.7f;
                        float tyaw = (float)crng.NextDouble() * Mathf.Tau;
                        float tsh = 0.8f + (float)crng.NextDouble() * 0.4f;
                        if (blockedSet.Contains(((int)px, (int)pz))) continue;
                        var tb = Basis.FromEuler(new Vector3(0, tyaw, 0))
                            .Scaled(new Vector3(ts * 1.4f, ts * 0.7f, ts));
                        talus.Add((new Transform3D(tb,
                            new Vector3(px, ts * 0.2f + GroundHeight(px, pz), pz)),
                            new Color(tsh, tsh, tsh)));
                    }
                    break;
            }
        }

        // Bridges: OPEN cells with a raised deck and kerb rails - the sim
        // paths straight across; the eye reads a river crossing. W4-18:
        // anisotropic UV stretch streaks the deck noise into planking.
        deckMat.Uv1Scale = new Vector3(1.5f, 10f, 1f);
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
            // W4-18: support piers descending into the water slab, an
            // under-deck cross-beam, and kerb posts breaking the rail line.
            foreach (float px in new[] { 0.22f, 0.78f })
                parent.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.55f, 0.14f) },
                    Position = new Vector3(bx + px, -0.28f, by + 0.5f),
                    MaterialOverride = ridgeMat,
                });
            parent.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.04f, 0.06f, 0.2f) },
                Position = new Vector3(bx + 0.5f, -0.02f, by + 0.5f),
                MaterialOverride = fenceMat,
            });
            foreach (float off in new[] { -0.46f, 0.46f })
                foreach (float pz in new[] { 0.15f, 0.85f })
                    parent.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.22f, 0.06f) },
                        Position = new Vector3(bx + 0.5f + off, 0.16f, by + pz),
                        MaterialOverride = fenceMat,
                    });
        }

        // W4-12: rubble scatter as ONE MultiMeshInstance3D at 10x density
        // (2200 instances, 1 draw call) instead of 220 per-piece nodes.
        var rubbleMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(37, 0.05f,
                dark: new Color(0.13f, 0.14f, 0.16f),
                light: new Color(0.22f, 0.235f, 0.26f)),
            Roughness = 0.95f,
            VertexColorUseAsAlbedo = true, // SetInstanceColor tints
        };
        var rubbleMesh = new BoxMesh { Size = new Vector3(1f, 0.7f, 1f) };
        rubbleMesh.Material = rubbleMat;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = rubbleMesh,
            InstanceCount = 2200 + talus.Count,
        };
        var zeroScale = new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero);
        for (int i = 0; i < 2200; i++)
        {
            float rx = (float)rng.NextDouble() * w, rz = (float)rng.NextDouble() * h;
            // Clustered by noise so the field does not read as uniform static.
            if (blockedSet.Contains(((int)rx, (int)rz))
                || _groundNoise.GetNoise2D(rx * 3f, rz * 3f) <= -0.15f)
            {
                mm.SetInstanceTransform(i, zeroScale);
                mm.SetInstanceColor(i, Colors.White);
                continue;
            }
            float s = 0.05f + (float)rng.NextDouble() * 0.16f;
            var basis = Basis.FromEuler(new Vector3(
                ((float)rng.NextDouble() - 0.5f) * 0.6f,
                (float)rng.NextDouble() * Mathf.Tau,
                ((float)rng.NextDouble() - 0.5f) * 0.6f))
                .Scaled(new Vector3(s * (1f + (float)rng.NextDouble()), s * 0.7f, s));
            mm.SetInstanceTransform(i, new Transform3D(basis,
                new Vector3(rx, s * 0.2f + GroundHeight(rx, rz), rz)));
            float sh = 0.8f + (float)rng.NextDouble() * 0.4f;
            mm.SetInstanceColor(i, new Color(sh, sh, sh));
        }
        for (int i = 0; i < talus.Count; i++)
        {
            mm.SetInstanceTransform(2200 + i, talus[i].Xform);
            mm.SetInstanceColor(2200 + i, talus[i].Shade);
        }
        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Name = "Rubble",
        });

        BuildScatter(parent, w, h, blockedSet, visual);
    }

    /// <summary>W4-15: prop variety scatter - dead-grass tufts, low rock
    /// clusters hugging ridge feet, flat plate debris around ruins. Three
    /// MultiMeshes, zero-scaled rejects, all riding GroundHeight.</summary>
    private static void BuildScatter(Node3D parent, int w, int h,
        HashSet<(int, int)> blockedSet, IReadOnlyDictionary<(int, int), char> visual)
    {
        var rng = new System.Random(2027);
        var density = _groundNoise!;
        var zeroScale = new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero);

        // Ridge boundary cells (as in the cliff pass) and ruin cells.
        var boundary = new List<(int, int)>();
        foreach (var (cx, cy) in blockedSet)
        {
            char k = visual.GetValueOrDefault((cx, cy), '#');
            if (k is 'w' or 'h' or 'r' or 'f') continue;
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                if (!blockedSet.Contains((cx + dx, cy + dy))) { boundary.Add((cx, cy)); break; }
        }
        var ruins = new List<(int, int)>();
        foreach (var (cell, k) in visual)
            if (k == 'r') ruins.Add(cell);

        // (1) TUFTS: two crossed vertical quads, alpha-scissored noise.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            st.SetNormal(n);
            st.SetUV(new Vector2(0, 1)); st.AddVertex(a);
            st.SetUV(new Vector2(1, 1)); st.AddVertex(b);
            st.SetUV(new Vector2(1, 0)); st.AddVertex(c);
            st.SetNormal(n);
            st.SetUV(new Vector2(0, 1)); st.AddVertex(a);
            st.SetUV(new Vector2(1, 0)); st.AddVertex(c);
            st.SetUV(new Vector2(0, 0)); st.AddVertex(d);
        }
        Quad(new Vector3(-0.11f, 0, 0), new Vector3(0.11f, 0, 0),
            new Vector3(0.11f, 0.16f, 0), new Vector3(-0.11f, 0.16f, 0), Vector3.Back);
        Quad(new Vector3(0, 0, 0.11f), new Vector3(0, 0, -0.11f),
            new Vector3(0, 0.16f, -0.11f), new Vector3(0, 0.16f, 0.11f), Vector3.Right);
        var tuftMesh = st.Commit();
        tuftMesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.23f, 0.20f, 0.14f),
            // Dark ramp point carries alpha 0 so the scissor cuts a ragged
            // tuft silhouette out of the noise (alpha-1 grey would never cut).
            AlbedoTexture = GrainTex(83, 0.4f,
                dark: new Color(0, 0, 0, 0), light: new Color(1, 1, 1)),
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.45f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 1.0f,
            VertexColorUseAsAlbedo = true,
        });
        var tufts = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = tuftMesh,
            InstanceCount = 900,
        };
        for (int i = 0; i < 900; i++)
        {
            float rx = (float)rng.NextDouble() * w, rz = (float)rng.NextDouble() * h;
            var cell = ((int)rx, (int)rz);
            if (blockedSet.Contains(cell) || visual.GetValueOrDefault(cell, '.') == 'w'
                || density.GetNoise2D(rx * 2.2f, rz * 2.2f) <= 0.05f)
            {
                tufts.SetInstanceTransform(i, zeroScale);
                tufts.SetInstanceColor(i, Colors.White);
                continue;
            }
            float sc = 0.7f + (float)rng.NextDouble() * 0.7f;
            var basis = Basis.FromEuler(new Vector3(0, (float)rng.NextDouble() * Mathf.Tau, 0))
                .Scaled(Vector3.One * sc);
            tufts.SetInstanceTransform(i, new Transform3D(basis,
                new Vector3(rx, GroundHeight(rx, rz), rz)));
            float sh = 0.8f + (float)rng.NextDouble() * 0.3f;
            tufts.SetInstanceColor(i, new Color(sh, sh, sh));
        }
        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = tufts,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Name = "Tufts",
        });

        // (2) ROCKS: low-poly squashed boulders, 60 percent hugging ridge feet.
        var rockMesh = new SphereMesh { Radius = 0.16f, Height = 0.22f, RadialSegments = 6, Rings = 3 };
        rockMesh.Material = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(91, 0.08f,
                dark: new Color(0.15f, 0.165f, 0.19f),
                light: new Color(0.26f, 0.28f, 0.31f)),
            Roughness = 0.95f,
            VertexColorUseAsAlbedo = true,
        };
        var rocks = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = rockMesh,
            InstanceCount = 350,
        };
        for (int i = 0; i < 350; i++)
        {
            float rx, rz;
            if (boundary.Count > 0 && rng.NextDouble() < 0.6)
            {
                var (bcx, bcy) = boundary[rng.Next(boundary.Count)];
                rx = bcx + 0.5f + ((float)rng.NextDouble() - 0.5f) * 2.8f;
                rz = bcy + 0.5f + ((float)rng.NextDouble() - 0.5f) * 2.8f;
            }
            else
            {
                rx = (float)rng.NextDouble() * w;
                rz = (float)rng.NextDouble() * h;
            }
            float yaw = (float)rng.NextDouble() * Mathf.Tau;
            var scale = new Vector3(
                1.0f + (float)rng.NextDouble() * 0.6f,
                0.4f + (float)rng.NextDouble() * 0.3f,
                0.8f + (float)rng.NextDouble() * 0.5f);
            if (rx < 0 || rz < 0 || rx >= w || rz >= h
                || blockedSet.Contains(((int)rx, (int)rz)))
            {
                rocks.SetInstanceTransform(i, zeroScale);
                rocks.SetInstanceColor(i, Colors.White);
                continue;
            }
            var basis = Basis.FromEuler(new Vector3(0, yaw, 0)).Scaled(scale);
            rocks.SetInstanceTransform(i, new Transform3D(basis,
                new Vector3(rx, 0.02f + GroundHeight(rx, rz), rz)));
            float sh = 0.85f + (float)rng.NextDouble() * 0.25f;
            rocks.SetInstanceColor(i, new Color(sh, sh, sh));
        }
        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = rocks,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Name = "Rocks",
        });

        // (3) PLATES: flat armour-plate debris, half of it around ruins.
        var plateMesh = new BoxMesh { Size = new Vector3(0.3f, 0.02f, 0.22f) };
        plateMesh.Material = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(37, 0.05f,
                dark: new Color(0.13f, 0.14f, 0.16f),
                light: new Color(0.22f, 0.235f, 0.26f)),
            Roughness = 0.95f,
            VertexColorUseAsAlbedo = true,
        };
        var plates = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = plateMesh,
            InstanceCount = 250,
        };
        for (int i = 0; i < 250; i++)
        {
            float rx, rz;
            if (ruins.Count > 0 && rng.NextDouble() < 0.5)
            {
                var (rcx, rcy) = ruins[rng.Next(ruins.Count)];
                rx = rcx + 0.5f + ((float)rng.NextDouble() - 0.5f) * 4f;
                rz = rcy + 0.5f + ((float)rng.NextDouble() - 0.5f) * 4f;
            }
            else
            {
                rx = (float)rng.NextDouble() * w;
                rz = (float)rng.NextDouble() * h;
            }
            float yaw = (float)rng.NextDouble() * Mathf.Tau;
            float tx = ((float)rng.NextDouble() - 0.5f) * 0.2f;
            float tz = ((float)rng.NextDouble() - 0.5f) * 0.2f;
            if (rx < 0 || rz < 0 || rx >= w || rz >= h
                || blockedSet.Contains(((int)rx, (int)rz)))
            {
                plates.SetInstanceTransform(i, zeroScale);
                plates.SetInstanceColor(i, Colors.White);
                continue;
            }
            var basis = Basis.FromEuler(new Vector3(tx, yaw, tz));
            plates.SetInstanceTransform(i, new Transform3D(basis,
                new Vector3(rx, 0.015f + GroundHeight(rx, rz), rz)));
            float sh = 0.8f + (float)rng.NextDouble() * 0.4f;
            plates.SetInstanceColor(i, new Color(sh, sh, sh));
        }
        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = plates,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Name = "Plates",
        });
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
