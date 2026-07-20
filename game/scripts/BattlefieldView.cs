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
                    // V1-03 (doc 25). ReflectedLightSource has always been Sky,
                    // so the sky IS the specular environment of every surface
                    // in the game, and at (0.015, 0.02, 0.032) that environment
                    // was black. Every ORM bake the project paid for was
                    // reflecting nothing. Lifting the sky is what makes those
                    // bakes pay for themselves.
                    SkyTopColor = new Color(0.060f, 0.080f, 0.130f),
                    SkyHorizonColor = new Color(0.22f, 0.19f, 0.16f),
                    SkyCurve = 0.12f,
                    SkyEnergyMultiplier = 1.6f,
                    GroundBottomColor = new Color(0.01f, 0.012f, 0.016f),
                    GroundHorizonColor = new Color(0.09f, 0.075f, 0.065f),
                    GroundCurve = 0.14f,
                    SunAngleMax = 30f,
                    SunCurve = 0.15f,
                },
            },
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            // V1-03 (doc 25). A constant ambient colour arrives identically
            // from every direction, so every surface the key light misses
            // receives the same value whatever way it faces, which is the
            // literal definition of flat shading. Under Sky the ambient is
            // directional and a hull's upward faces separate from its
            // downward ones for free. AmbientLightColor is DELETED rather
            // than left at its old value: it is unread under Sky and leaving
            // it would invite a future reader to tune a dead number.
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1.0f,
            // NOT lowered, and this is the clause three separate audits got
            // wrong: the shipped diffuse atlas has a median of linear 0.037,
            // so the frame is already too dark to cut ambient in the same
            // change that lifts the sky.
            //
            // MEASURED, and worth knowing before anyone tunes this: under
            // AmbientSource.Sky with AmbientLightSkyContribution at 1.0, this
            // value is DEAD. Raising it from 0.4 to 1.0 moved mean open-ground
            // luminance at CAM-A by 0.03 out of 255, which is nothing. The
            // ambient level is now set by SkyTopColor, SkyHorizonColor and
            // SkyEnergyMultiplier and by nothing else. It is left at 0.4 per
            // V1-03 clause 4 rather than deleted, so that dropping sky
            // contribution below 1.0 has something sensible to fall back on.
            AmbientLightEnergy = 0.4f,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            // V1-07 (doc 25), the single retune, chosen against captures and
            // recorded here as the ticket requires. 1.35 was set when between
            // forty-seven and seventy per cent of every pixel was in-scattered
            // fog ADDING uniform brightness; V1-01 took that away, and the
            // measured result was mean open-ground luminance at CAM-A falling
            // from 78 to 55 out of 255 against doc 22's ratified 90 to 135
            // band, which the baseline never met either.
            //
            // Exposure rather than ambient, fill or key, for three reasons.
            // The clause above is the first: ambient energy does nothing in
            // this configuration. The second is that exposure multiplies where
            // the fog added, so it restores the level without re-flattening
            // the contrast the fog cut just bought. The third is that doc 22's
            // C-10 clause 6 already names TonemapExposure as the single
            // permitted compensating knob and requires the value be recorded,
            // and light COLOURS are C-10's business, not this ticket's.
            //
            // 1.9 gave 71, 2.4 gave 83, 2.9 gives 94, which is inside the
            // band. NOTE FOR C-10: it raises total directional energy by
            // fourteen per cent, so this number is the one to bring back down,
            // once, if the histogram then overshoots.
            //
            // V2-03 retune (doc 25 wave V2). Brought down from 2.9 to 2.85,
            // and the reason it comes down only that far is itself the finding.
            // V1 raised exposure to replace the brightness the fog had added;
            // V2's bake was expected to brighten the frame back and let this
            // fall further. Measured, it did not. The AO-multiply removal and
            // the false-specular removal brighten the baked UNITS, not the
            // ground, because the ground is a runtime shader carrying no baked
            // map, so whole-frame CAM-A luminance actually fell slightly
            // (59.7 to 57.1). Exposure tracks the ground, which is the knob's
            // ratified constraint: the new bake gives CAM-A open-ground
            // luminance 92.0 at 2.9, 90.0 at 2.8 and 85.8 at 2.6, so anything
            // below about 2.8 breaks doc 22 C-10's 90-to-135 floor. And it is
            // the wrong tool for the units regardless: they read hot at 163
            // mean, but AgX compresses them in its shoulder, so 2.9 to 2.6
            // moves them only 163 to 155. 2.85 lands CAM-A ground at 91.0, a
            // safe level inside the band, and trims the frame a touch. Taming
            // the units belongs to doc 22's C-05 chroma pass, not to exposure.
            TonemapExposure = 2.85f,
            AdjustmentEnabled = true,
            AdjustmentBrightness = 1.03f,
            AdjustmentContrast = 1.14f,
            AdjustmentSaturation = 1.12f,
            SsaoEnabled = true,
            SsaoRadius = 1.5f,
            // V2-02 (doc 25). 2.5 and 1.8, against Godot's defaults of 2.0
            // and 1.5, were tuned to be visible against crevices that were
            // ALREADY darkened: the bake multiplied ambient occlusion into the
            // base colour at eighty-five per cent and shipped the same AO
            // again in the ORM red channel. With that double count removed
            // from the assets, the old values crush the crevices to black
            // holes rather than shading them.
            SsaoIntensity = 1.6f,
            SsaoPower = 1.4f,
            SsaoDetail = 0.6f,
            SsaoHorizon = 0.06f,
            SsaoSharpness = 0.98f,
            SsaoLightAffect = 0.1f,
            SsilEnabled = true,
            SsilRadius = 3.0f,
            SsilIntensity = 1.2f,
            SsilSharpness = 0.98f,
            SsilNormalRejection = 1.0f,
            // V1-06 (doc 25): OFF. Screen-space reflections can only reflect
            // what is already on screen, and at a fixed -50 pitch almost
            // nothing is on screen to reflect onto the ground. Every terrain
            // material in this file is roughness 0.85 to 1.0 and a rough
            // surface returns nothing from SSR, so the full 56-step cost was
            // being paid for one water slab, which keeps its sky reflection
            // and at this camera was showing mostly that anyway. The settings
            // stay so that turning it back on is one word.
            SsrEnabled = false,
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
            // V1-01 (doc 25), and this is the largest single change in the
            // wave. An RTS has no near field: the pitch is fixed at -50, so
            // the nearest thing on screen is H/sin(50) away, which is 28.7 m
            // at the start height and 54.8 m at maximum zoom. At the old
            // density of 0.022 that put transmittance between 0.53 and 0.30,
            // meaning between forty-seven and seventy per cent of the radiance
            // of EVERY pixel was uniform in-scattered fog rather than the
            // battlefield, and uniform because in a top-down view everything
            // sits at roughly the same depth. Measured with the LOOK-01
            // harness by setting this to zero: the fog was adding 37.5/255 of
            // flat luminance at CAM-A and holding the whole frame inside an
            // 18-degree hue span, which opened to 223 degrees without it.
            //
            // This is a DENSITY change, not a deletion. The fog is doing real
            // atmospheric work at the far edge and the storm mood depends on
            // it; 0.010 lifts transmittance at 28.7 m from 0.53 to 0.75.
            VolumetricFogEnabled = true,
            VolumetricFogDensity = 0.010f,
            // Less saturated and darker, so what fog remains reads as distance
            // rather than as a scrim laid over the lens.
            VolumetricFogAlbedo = new Color(0.46f, 0.50f, 0.58f),
            VolumetricFogAnisotropy = 0.35f,
            // The length is not the problem. The density over the unavoidable
            // minimum distance is.
            VolumetricFogLength = 110f,
            VolumetricFogDetailSpread = 2.5f,
            VolumetricFogAmbientInject = 0.08f,
            // Previously unset and therefore zero: the fog took no light from
            // the scene at all, which is why it read as a uniform tint rather
            // than as air. With this the explosions and the superweapon put
            // coloured haze into the volume around them.
            // Note the capitalisation: Godot's C# binding spells this
            // VolumetricFogGIInject, not ...GiInject.
            VolumetricFogGIInject = 0.6f,
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

    // W1-11: shared contact-shadow blob texture.
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

    // V-TERRAIN: the biome field. Grass, sand and rock blend weights plus a
    // wetness band, one entry per map cell, generated once from the map and
    // read by BOTH the ground shader (as a texture) AND the grass, tree and
    // rock-formation placement (through BiomeAt), so every layer agrees on
    // where each biome is. Presentation only: it is derived from blockedCells
    // and the visual legend, which are read, never written, and it feeds no
    // sim path. It cannot change passability.
    private static float[]? _biomeGrass, _biomeSand, _biomeRock, _biomeWet;
    private static int _biomeW, _biomeH;
    private static bool _temperate;

    /// <summary>The blend weights and wetness at a world XZ, for seeding grass,
    /// trees and rock formations. Weights are raw (the shader renormalises);
    /// callers compare them or threshold them. Presentation only.</summary>
    public readonly record struct Biome(float Grass, float Sand, float Rock, float Wet);

    public static Biome BiomeAt(float x, float z)
    {
        if (_biomeGrass == null || _biomeSand == null || _biomeRock == null || _biomeWet == null)
            return new Biome(0f, 1f, 0f, 0f);
        int cx = Mathf.Clamp((int)x, 0, _biomeW - 1);
        int cz = Mathf.Clamp((int)z, 0, _biomeH - 1);
        int i = cz * _biomeW + cx;
        return new Biome(_biomeGrass[i], _biomeSand[i], _biomeRock[i], _biomeWet[i]);
    }

    /// <summary>Is the base biome temperate (grass) rather than arid (sand)?
    /// A map with any water is temperate; a map with none is arid. This is the
    /// only "per-map biome" input and it needs no new map metadata and no sim
    /// change.</summary>
    public static bool IsTemperate => _temperate;

    /// <summary>
    /// Build the biome control map from the map alone (deterministic, no RNG,
    /// no sim coupling). Channels: R grass weight, G sand weight, B rock weight,
    /// A wetness. The base biome is temperate if the map has water, else arid;
    /// within the map, sand forms beaches in a band around water and dry patches
    /// by macro noise, rock forms on the feet of hills/ruins/ridges and by macro
    /// outcrop noise, grass fills the rest of a temperate map. One light blur
    /// pass smooths the biome boundaries before the shader's linear filter.
    /// </summary>
    private static ImageTexture BuildBiomeMap(int w, int h,
        HashSet<(int, int)> blockedSet, HashSet<(int, int)> waterCells,
        IReadOnlyDictionary<(int, int), char> visual)
    {
        _biomeW = w; _biomeH = h;
        _temperate = waterCells.Count > 0;
        int n = w * h;
        _biomeGrass = new float[n];
        _biomeSand = new float[n];
        _biomeRock = new float[n];
        _biomeWet = new float[n];

        // Chebyshev distance to the nearest water cell (for beaches and the wet
        // band) and to the nearest rock-seeding blocker: hills 'h', ruins 'r'
        // and blocked '#' feet, but not water and not fences. Bounded BFS.
        int[] Bfs(IEnumerable<(int, int)> seeds, int cap)
        {
            var d = new int[n];
            for (int i = 0; i < n; i++) d[i] = 99;
            var q = new Queue<(int, int)>();
            foreach (var (sx, sy) in seeds)
                if (sx >= 0 && sx < w && sy >= 0 && sy < h && d[sy * w + sx] != 0)
                { d[sy * w + sx] = 0; q.Enqueue((sx, sy)); }
            while (q.Count > 0)
            {
                var (cx, cy) = q.Dequeue();
                int cd = d[cy * w + cx];
                if (cd >= cap) continue;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        if (d[ny * w + nx] > cd + 1) { d[ny * w + nx] = cd + 1; q.Enqueue((nx, ny)); }
                    }
            }
            return d;
        }

        var rockSeeds = new List<(int, int)>();
        foreach (var cell in blockedSet)
        {
            char k = visual.GetValueOrDefault(cell, '#');
            if (k == 'w' || k == 'f') continue;
            rockSeeds.Add(cell);
        }
        int[] distWater = Bfs(waterCells, 4);
        int[] distRock = Bfs(rockSeeds, 3);

        // Macro fields: rocky outcrops and dry (sand) patches, fixed seeds.
        var outcrop = new FastNoiseLite
        {
            Seed = 911, Frequency = 0.05f, FractalOctaves = 3,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        };
        var dry = new FastNoiseLite
        {
            Seed = 733, Frequency = 0.035f, FractalOctaves = 3,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        };

        for (int cy = 0; cy < h; cy++)
            for (int cx = 0; cx < w; cx++)
            {
                int i = cy * w + cx;
                float grass, sand, rock;
                if (_temperate) { grass = 1.0f; sand = 0.05f; rock = 0.05f; }
                else { grass = 0.05f; sand = 0.75f; rock = 0.28f; }

                // Rock: the feet of blockers, plus macro outcrops.
                int dr = distRock[i];
                if (dr <= 2) rock = Mathf.Max(rock, 1.0f - dr * 0.4f);
                float on = outcrop.GetNoise2D(cx, cy) * 0.5f + 0.5f;
                rock = Mathf.Max(rock, Mathf.SmoothStep(0.60f, 0.84f, on) * (_temperate ? 0.85f : 1.0f));

                // Sand: beaches in a band around water, plus dry patches.
                int dw = distWater[i];
                if (dw >= 1 && dw <= 3) sand = Mathf.Max(sand, Mathf.Clamp(1.0f - (dw - 1) * 0.42f, 0f, 1f));
                float dn = dry.GetNoise2D(cx, cy) * 0.5f + 0.5f;
                sand = Mathf.Max(sand, Mathf.SmoothStep(0.58f, 0.84f, dn) * (_temperate ? 0.55f : 0.95f));

                // Wetness: the shore land just outside the water (dw 1..3), 0 on
                // the water itself (that cell shows the slab, not ground).
                float wet = dw == 0 ? 0f : Mathf.Clamp((3f - dw) / 2f, 0f, 1f);

                // Grass recedes where sand or rock dominates.
                grass *= (1f - 0.9f * sand) * (1f - 0.9f * rock);

                _biomeGrass[i] = grass;
                _biomeSand[i] = sand;
                _biomeRock[i] = rock;
                _biomeWet[i] = wet;
            }

        // One separable-ish 3x3 box blur so the biome boundaries feather before
        // the shader's bilinear filter, and so grass/tree/rock placement reading
        // BiomeAt sees the same smoothed field the ground shows.
        float[] Blur(float[] src)
        {
            var dst = new float[n];
            for (int cy = 0; cy < h; cy++)
                for (int cx = 0; cx < w; cx++)
                {
                    float s = 0f; int c = 0;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                            s += src[ny * w + nx]; c++;
                        }
                    dst[cy * w + cx] = s / c;
                }
            return dst;
        }
        _biomeGrass = Blur(_biomeGrass);
        _biomeSand = Blur(_biomeSand);
        _biomeRock = Blur(_biomeRock);
        _biomeWet = Blur(_biomeWet);

        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int cy = 0; cy < h; cy++)
            for (int cx = 0; cx < w; cx++)
            {
                int i = cy * w + cx;
                img.SetPixel(cx, cy, new Color(
                    _biomeGrass[i], _biomeSand[i], _biomeRock[i], _biomeWet[i]));
            }
        return ImageTexture.CreateFromImage(img);
    }

    // V-TERRAIN: the wind/wave phase. TickWater used to scroll only the water
    // UV; it now advances one phase and pushes it to the ground-adjacent shaders
    // that animate (water ripples, grass sway). Driven from _Process, so it
    // FREEZES when the look-dev harness pauses the tree, which is what keeps two
    // captures byte-identical. TIME would keep advancing while paused.
    private static ShaderMaterial? _waterShader;
    private static readonly List<ShaderMaterial> _windShaders = new();
    private static float _windPhase;

    public static void TickWater(double delta)
    {
        _windPhase += (float)delta;
        if (_waterShader != null)
            _waterShader.SetShaderParameter("wave_time", _windPhase);
        foreach (var m in _windShaders)
            m.SetShaderParameter("wind_phase", _windPhase);
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
        // V-TERRAIN: a rebuild starts a fresh set of animated shaders, so drop
        // any references the previous build left in the wind/wave tick.
        _windShaders.Clear();
        _waterShader = null;
        // W4-13: the full blocked set, hoisted before any loop so ridge
        // cells can classify their neighbours.
        var blockedSet = new HashSet<(int, int)>(blockedCells);
        // MAP-03: the scatter counts below were tuned by eye at 96x64, as
        // absolute numbers, so a 192x128 map spread the same confetti over
        // four times the ground and read as bare. Scale by area instead. At
        // 96x64 this is exactly 1.0, so the three shipped maps are untouched.
        float densityScale = (w * h) / 6144f;
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

        // V-TERRAIN: the biome control map (grass/sand/rock/wetness), generated
        // deterministically from the map itself, and the biome ground shader
        // that blends the three real biomes it drives. Replaces the old
        // single-arid-material splat shader (dust/rock/gravel, all grey-brown).
        var biomeTex = BuildBiomeMap(w, h, blockedSet, waterCells, visual);
        var groundShader = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://shaders/ground_biome.gdshader"),
        };
        groundShader.SetShaderParameter("noise_a", RawNoise(101, 0.02f));
        groundShader.SetShaderParameter("noise_b", RawNoise(103, 0.02f));
        groundShader.SetShaderParameter("detail_normal", GrainTex(11, 0.05f, normalMap: true));
        groundShader.SetShaderParameter("biome_map", biomeTex);
        groundShader.SetShaderParameter("map_size", new Vector2(w, h));

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
        // V-TERRAIN: the river is a real water surface now, not a dark slab.
        // A custom shader gives it depth colour, a fresnel edge, scrolling
        // ripple normals and a shore foam line (game/shaders/water.gdshader).
        // The basin geometry (the sunken per-cell slabs below) is unchanged;
        // only the material is replaced. It is TRANSPARENT so the shader can
        // read the scene depth behind it, which is what SSR being off (V1-06)
        // now allows without cost. wave_time is pushed by TickWater so it
        // freezes under the look-dev pause and captures stay byte-identical.
        var waterShader = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://shaders/water.gdshader"),
        };
        waterShader.SetShaderParameter("ripple_normal", GrainTex(53, 0.09f, normalMap: true));
        _waterShader = waterShader;
        // One seamless surface over every water cell at the waterline, so the
        // transparent shader has no per-tile seam to double-blend.
        BuildWaterSurface(parent, waterCells, waterShader);
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

        // MAP-01: every per-cell dressing piece below used to be its own
        // MeshInstance3D, so terrain cost one draw call per piece and a
        // 192x128 map wanted about 6100 of them. The pieces are only ever a
        // box or a sphere, so they batch into one MultiMesh per material:
        // same meshes, same transforms, same materials, same picture, an
        // eleventh of the nodes. Emit collects; the loops below fill it; the
        // batches are materialised once after the bridge loop.
        var box = new BoxMesh { Size = Vector3.One };      // UNIT box; instance scale carries the real size
        var batches = new Dictionary<Material, List<Transform3D>>();

        // Hills need their own treatment, and this is the one real trap in the
        // batching. Godot builds a MultiMesh instance's normal matrix as
        // mat3(instance_transform) rather than its inverse-transpose, so a
        // NON-UNIFORM per-instance scale skews the normals. On a box that is
        // harmless: a box's face normals lie along the scale axes, so scaling
        // by (a,b,c) sends (1,0,0) to (a,0,0), which renormalises straight back.
        // On a sphere it is not: the normals are not axis-aligned, and squashing
        // a unit sphere per-instance turns these matte Roughness-0.95 mounds
        // into glossy plastic. MEASURED, not theorised - the offscreen A/B of
        // skirmish-03 shows it plainly, and forcing the scale uniform removes it.
        // So bucket the hills by their squash ratio k = (hh/2)/hr, give each
        // bucket a sphere BUILT at that ratio (correct baked normals), and keep
        // the per-instance scale UNIFORM, where mat3() is exactly right.
        // Eight buckets puts the height error under 4%, which is invisible;
        // the shading error it removes is not. Cost: <=8 draw calls, not one.
        const int hillBuckets = 8;
        const float kMin = 0.577f, kMax = 1.048f;  // (hh/2)/hr over the generator's ranges
        var hillBins = new List<Transform3D>[hillBuckets];
        // Scale is composed on the RIGHT (basis * FromScale) so it applies in
        // the piece's LOCAL frame, which is what Node3D.Scale did and what
        // BoxMesh.Size means. Basis.Scaled() would apply it in the global
        // frame instead: measured, that turns the 0.02-thick shore ramps into
        // 0.118-thick wedges at the wrong tilt. Do not "simplify" this.
        void Emit(Material mat, Vector3 size, Vector3 pos, Vector3 rotEuler)
        {
            if (!batches.TryGetValue(mat, out var l)) batches[mat] = l = new List<Transform3D>();
            l.Add(new Transform3D(Basis.FromEuler(rotEuler) * Basis.FromScale(size), pos));
        }

        foreach (var (bx, by) in blockedCells)
        {
            char kind = visual.GetValueOrDefault((bx, by), '#');
            switch (kind)
            {
                case 'w':
                    // Water: a sunken reflective slab; the shore lip is the
                    // ground plane edge reading against it.
                    // V-TERRAIN: the water surface is now ONE seamless mesh
                    // (BuildWaterSurface, below), not a per-cell slab, so the
                    // transparent material has no tile seams to double-blend.
                    // This 'w' case keeps only the shore dressing.
                    // W4-14 Part B/C: on each edge that meets ground, a wet
                    // ramp descending from the ground lip to the waterline,
                    // and a faint foam line just inside the water cell.
                    // Ramp high edges sit at y~0 against the ground lip;
                    // rotation signs verified against the capture.
                    if (!waterCells.Contains((bx - 1, by)))
                    {
                        Emit(shoreMat, new Vector3(0.36f, 0.02f, 1.04f),
                            new Vector3(bx + 0.17f, -0.05f, by + 0.5f), new Vector3(0, 0, -0.33f));
                        Emit(foamMat, new Vector3(0.07f, 0.012f, 1.04f),
                            new Vector3(bx + 0.04f, -0.065f, by + 0.5f), Vector3.Zero);
                    }
                    if (!waterCells.Contains((bx + 1, by)))
                    {
                        Emit(shoreMat, new Vector3(0.36f, 0.02f, 1.04f),
                            new Vector3(bx + 0.83f, -0.05f, by + 0.5f), new Vector3(0, 0, 0.33f));
                        Emit(foamMat, new Vector3(0.07f, 0.012f, 1.04f),
                            new Vector3(bx + 0.96f, -0.065f, by + 0.5f), Vector3.Zero);
                    }
                    if (!waterCells.Contains((bx, by - 1)))
                    {
                        Emit(shoreMat, new Vector3(1.04f, 0.02f, 0.36f),
                            new Vector3(bx + 0.5f, -0.05f, by + 0.17f), new Vector3(0.33f, 0, 0));
                        Emit(foamMat, new Vector3(1.04f, 0.012f, 0.07f),
                            new Vector3(bx + 0.5f, -0.065f, by + 0.04f), Vector3.Zero);
                    }
                    if (!waterCells.Contains((bx, by + 1)))
                    {
                        Emit(shoreMat, new Vector3(1.04f, 0.02f, 0.36f),
                            new Vector3(bx + 0.5f, -0.05f, by + 0.83f), new Vector3(-0.33f, 0, 0));
                        Emit(foamMat, new Vector3(1.04f, 0.012f, 0.07f),
                            new Vector3(bx + 0.5f, -0.065f, by + 0.96f), Vector3.Zero);
                    }
                    break;
                case 'h':
                    // Hill: a broad smooth mound (sphere squashed flat), so
                    // clusters merge into rolling high ground. The two
                    // NextDouble calls stay in their original order: the rng is
                    // shared across every cell, so reordering them would
                    // reshuffle the whole map.
                    float hr = 1.05f + (float)rng.NextDouble() * 0.25f;
                    float hh = 1.5f + (float)rng.NextDouble() * 0.7f;
                    // Bucket by squash ratio; the bucket's mesh carries the
                    // squash, the instance carries only a uniform scale.
                    int hb = Mathf.Clamp(
                        (int)((hh / 2f / hr - kMin) / (kMax - kMin) * hillBuckets),
                        0, hillBuckets - 1);
                    (hillBins[hb] ??= new List<Transform3D>()).Add(new Transform3D(
                        Basis.FromScale(new Vector3(hr, hr, hr)),
                        new Vector3(bx + 0.5f, -0.15f, by + 0.5f)));
                    break;
                case 'r':
                    // Ruin: broken wall stubs at jittered heights - a dead
                    // settlement to fight through.
                    float wh = 0.35f + (float)rng.NextDouble() * 0.5f;
                    float ryaw = ((float)rng.NextDouble() - 0.5f) * 0.3f;
                    Emit(ruinMat, new Vector3(0.9f, wh, 0.9f),
                        new Vector3(bx + 0.5f, wh / 2f, by + 0.5f), new Vector3(0, ryaw, 0));
                    if (rng.NextDouble() > 0.6)
                    {
                        float sx = bx + 0.3f + (float)rng.NextDouble() * 0.4f;
                        float sz = by + 0.3f + (float)rng.NextDouble() * 0.4f;
                        Emit(ruinMat, new Vector3(0.4f, wh * 1.6f, 0.3f),
                            new Vector3(sx, wh * 0.8f, sz), Vector3.Zero);
                    }
                    break;
                case 'f':
                    // Fence: posts and two rails along the cell.
                    for (int px = 0; px < 2; px++)
                        Emit(fenceMat, new Vector3(0.07f, 0.5f, 0.07f),
                            new Vector3(bx + 0.2f + px * 0.6f, 0.25f, by + 0.5f), Vector3.Zero);
                    for (int rail = 0; rail < 2; rail++)
                        Emit(fenceMat, new Vector3(1.0f, 0.05f, 0.05f),
                            new Vector3(bx + 0.5f, 0.18f + rail * 0.22f, by + 0.5f), Vector3.Zero);
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
                    // Three crng draws for the jitter, in their original X/Y/Z
                    // order - crng is per-cell, but the order still decides
                    // which value lands on which axis.
                    float jx = ((float)crng.NextDouble() - 0.5f) * 0.10f;
                    float jy = ((float)crng.NextDouble() - 0.5f) * 0.5f;
                    float jz = ((float)crng.NextDouble() - 0.5f) * 0.10f;
                    Emit(ridgeMat, new Vector3(1.18f, jh, 1.18f),
                        new Vector3(bx + 0.5f, jh / 2f - 0.06f, by + 0.5f),
                        new Vector3(jx, jy, jz));
                    if (!boundary) break;
                    // Boundary cells: stepped strata plus a dust-capped top.
                    Emit(cliffBaseMat, new Vector3(1.30f, jh * 0.38f, 1.30f),
                        new Vector3(bx + 0.5f, jh * 0.19f - 0.06f, by + 0.5f),
                        new Vector3(0, ((float)crng.NextDouble() - 0.5f) * 0.12f, 0));
                    Emit(ridgeMat, new Vector3(1.22f, jh * 0.30f, 1.22f),
                        new Vector3(bx + 0.5f, jh * 0.55f - 0.06f, by + 0.5f),
                        new Vector3(0, ((float)crng.NextDouble() - 0.5f) * 0.18f, 0));
                    // Dust-capped mesa read: the ground shader on a thin slab,
                    // so the cap picks up the same world-space splat as the
                    // ground below it. MODEL_MATRIX carries the MultiMesh
                    // instance transform, so wpos is unchanged by batching.
                    Emit(groundShader, new Vector3(1.08f, 0.07f, 1.08f),
                        new Vector3(bx + 0.5f, jh - 0.04f, by + 0.5f), Vector3.Zero);
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
            Emit(deckMat, new Vector3(1.04f, 0.1f, 1.04f),
                new Vector3(bx + 0.5f, 0.05f, by + 0.5f), Vector3.Zero);
            foreach (float off in new[] { -0.46f, 0.46f })
                Emit(fenceMat, new Vector3(0.08f, 0.16f, 1.04f),
                    new Vector3(bx + 0.5f + off, 0.14f, by + 0.5f), Vector3.Zero);
            // W4-18: support piers descending into the water slab, an
            // under-deck cross-beam, and kerb posts breaking the rail line.
            foreach (float px in new[] { 0.22f, 0.78f })
                Emit(ridgeMat, new Vector3(0.14f, 0.55f, 0.14f),
                    new Vector3(bx + px, -0.28f, by + 0.5f), Vector3.Zero);
            Emit(fenceMat, new Vector3(1.04f, 0.06f, 0.2f),
                new Vector3(bx + 0.5f, -0.02f, by + 0.5f), Vector3.Zero);
            foreach (float off in new[] { -0.46f, 0.46f })
                foreach (float pz in new[] { 0.15f, 0.85f })
                    Emit(fenceMat, new Vector3(0.06f, 0.22f, 0.06f),
                        new Vector3(bx + 0.5f + off, 0.16f, by + pz), Vector3.Zero);
        }

        // MAP-01: both loops have contributed, so materialise the batches.
        // One MultiMesh per material, every box-shaped batch sharing the unit
        // box (a MultiMesh carries exactly one Mesh, which is precisely why
        // batching by Material is the right and sufficient key).
        // CastShadow stays at its default On: the shipped per-cell nodes cast
        // shadows and the doc-20 look leans on ridge and hill shadows. Only
        // the scatter MultiMeshes below turn shadows off, as they already did.
        // UseColors stays false: none of the per-cell terrain tints instances.
        foreach (var (mat, xforms) in batches)
        {
            var tmm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = box,
                InstanceCount = xforms.Count,
            };
            for (int i = 0; i < xforms.Count; i++) tmm.SetInstanceTransform(i, xforms[i]);
            parent.AddChild(new MultiMeshInstance3D
            {
                Multimesh = tmm,
                MaterialOverride = mat,
                Name = "TerrainBatch",
            });
        }
        // One batch per populated hill bucket, each with a sphere built at that
        // bucket's squash so its baked normals are right under a uniform scale.
        for (int b = 0; b < hillBuckets; b++)
        {
            var bin = hillBins[b];
            if (bin == null || bin.Count == 0) continue;
            float kMid = kMin + (b + 0.5f) * (kMax - kMin) / hillBuckets;
            var hmm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new SphereMesh
                {
                    Radius = 1f, Height = 2f * kMid, RadialSegments = 14, Rings = 7,
                },
                InstanceCount = bin.Count,
            };
            for (int i = 0; i < bin.Count; i++) hmm.SetInstanceTransform(i, bin[i]);
            parent.AddChild(new MultiMeshInstance3D
            {
                Multimesh = hmm,
                MaterialOverride = hillMat,
                Name = $"Hills{b}",
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
        // MAP-03: 2200 was the 96x64 count. It appears three times - the
        // instance count, the loop bound, and the base index the talus fold
        // writes above - and all three must move together or the fold indexes
        // off the end of the MultiMesh.
        int rubbleN = Scaled(2200, densityScale);
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = rubbleMesh,
            InstanceCount = rubbleN + talus.Count,
        };
        var zeroScale = new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero);
        for (int i = 0; i < rubbleN; i++)
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
            // V3-04 (doc 25): the minimum rubble scale rises from 0.05 to 0.12
            // so no instance is a sub-pixel fleck that flickers in and out of
            // sample coverage as the camera pans. The 0.16 spread is kept, so
            // the range is now 0.12 to 0.28.
            float s = 0.12f + (float)rng.NextDouble() * 0.16f;
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
            mm.SetInstanceTransform(rubbleN + i, talus[i].Xform);
            mm.SetInstanceColor(rubbleN + i, talus[i].Shade);
        }
        // MAP-03: the fold above is the one index-arithmetic trap in this
        // file. If the count and the base ever disagree the write runs off
        // the end, so say so out loud rather than trusting the edit.
        if (mm.InstanceCount != rubbleN + talus.Count)
            throw new System.InvalidOperationException(
                $"rubble MultiMesh sized {mm.InstanceCount} but holds {rubbleN} + {talus.Count} talus");
        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Name = "Rubble",
            // V3-04 (doc 25): range-cull the whole field rather than deleting
            // instances (MAP-03 scaled the count up on purpose). Godot measures
            // the visibility range from the camera to the node's AABB centre,
            // which for a map-spanning MultiMesh is the map centre, so with the
            // three frozen cameras roughly 25 m from that centre at CAM-A/CAM-C
            // and 47 m at CAM-B this shows the field at play height and close in
            // and hides it at max zoom, where each fleck is sub-pixel and only
            // contributes pan shimmer. This is the shipped grass pattern
            // (BuildGrass uses End 40); 38 with a 6 m Self fade band is the same
            // idea a touch tighter. The FOV change does not move the cameras, so
            // it does not disturb these distances.
            VisibilityRangeEnd = 38f,
            VisibilityRangeEndMargin = 6f,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        });

        BuildScatter(parent, w, h, blockedSet, visual, densityScale);
    }

    /// <summary>MAP-03: scatter counts were tuned as absolutes at 96x64
    /// (6144 cells). Scale them by map area so per-cell density is what the
    /// W4-12/W4-15 passes chose, whatever size the map is. At 96x64 the
    /// factor is exactly 1.0 and every count is its shipped value.</summary>
    private static int Scaled(int baseCount, float densityScale)
        => Mathf.RoundToInt(baseCount * densityScale);

    /// <summary>V-TERRAIN: one seamless water surface over every water cell at
    /// the waterline (y = -0.07, the old slab's top). It replaces the per-cell
    /// slabs so the transparent water shader has no overlapping tile edges to
    /// double-blend into a grid of dark seams. Adjacent cell quads share exact
    /// corner coordinates, so the surface has no gap and no overlap. The basin
    /// (the sunken ground under the water, GroundHeight -0.22) is unchanged, so
    /// the shader still reads a real depth beneath the surface.</summary>
    private static void BuildWaterSurface(Node3D parent, HashSet<(int, int)> waterCells,
        ShaderMaterial mat)
    {
        if (waterCells.Count == 0) return;
        const float y = -0.07f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        st.SetTangent(new Plane(1, 0, 0, 1)); // +X tangent; NORMAL_MAP needs one
        foreach (var (cx, cy) in waterCells)
        {
            var a = new Vector3(cx, y, cy);
            var b = new Vector3(cx + 1, y, cy);
            var c = new Vector3(cx, y, cy + 1);
            var d = new Vector3(cx + 1, y, cy + 1);
            // Clockwise front-face winding, matching the ground mesh so the top
            // faces up (Godot front faces wind clockwise, the ground's own note).
            st.SetUV(new Vector2(cx, cy)); st.AddVertex(a);
            st.SetUV(new Vector2(cx + 1, cy)); st.AddVertex(b);
            st.SetUV(new Vector2(cx, cy + 1)); st.AddVertex(c);
            st.SetUV(new Vector2(cx + 1, cy)); st.AddVertex(b);
            st.SetUV(new Vector2(cx + 1, cy + 1)); st.AddVertex(d);
            st.SetUV(new Vector2(cx, cy + 1)); st.AddVertex(c);
        }
        parent.AddChild(new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Name = "Water",
        });
    }

    /// <summary>W4-15: prop variety scatter - dead-grass tufts, low rock
    /// clusters hugging ridge feet, flat plate debris around ruins. Three
    /// MultiMeshes, zero-scaled rejects, all riding GroundHeight.</summary>
    private static void BuildScatter(Node3D parent, int w, int h,
        HashSet<(int, int)> blockedSet, IReadOnlyDictionary<(int, int), char> visual,
        float densityScale)
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

        // V-TERRAIN: real grass on the grassy biome, tree copses on the water
        // banks and map edges, and rock formations on the rocky biome and cliff
        // feet. Each reads BiomeAt so it lands where the ground shows its biome.
        // Own rng streams (2028/2029/2030), so they do not perturb the
        // tuft/rock/plate scatter's 2027 order.
        BuildGrass(parent, w, h, blockedSet, visual, densityScale);
        BuildTrees(parent, w, h, blockedSet, visual);
        BuildRockFormations(parent, w, h, blockedSet, visual, densityScale);

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
        int tuftN = Scaled(900, densityScale);
        var tufts = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = tuftMesh,
            InstanceCount = tuftN,
        };
        for (int i = 0; i < tuftN; i++)
        {
            float rx = (float)rng.NextDouble() * w, rz = (float)rng.NextDouble() * h;
            var cell = ((int)rx, (int)rz);
            // V-TERRAIN: these are DEAD-straw tufts, so keep them off grassy
            // ground (the live grass MultiMesh owns that) and let them dress the
            // dry, sandy and rocky biome instead.
            if (blockedSet.Contains(cell) || visual.GetValueOrDefault(cell, '.') == 'w'
                || BiomeAt(rx, rz).Grass > 0.45f
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
            // V3-04 (doc 25): the dead-straw tufts are half a pixel to two
            // pixels each at max zoom, so range-cull them exactly as the rubble
            // above and the live grass already do.
            VisibilityRangeEnd = 38f,
            VisibilityRangeEndMargin = 6f,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
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
        int rockN = Scaled(350, densityScale);
        var rocks = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = rockMesh,
            InstanceCount = rockN,
        };
        for (int i = 0; i < rockN; i++)
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
        int plateN = Scaled(250, densityScale);
        var plates = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = plateMesh,
            InstanceCount = plateN,
        };
        for (int i = 0; i < plateN; i++)
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

    // V-TERRAIN: a procedurally drawn blade card - a handful of tapering
    // vertical blades with a soft root-to-tip shade baked into RGB and the blade
    // shape in alpha. Deterministic (fixed seed), cached. UV.y = 1 is the root
    // (the Quad helper puts UV (0,1) at the bottom verts), so the image bottom
    // rows are the roots and the top rows are the tips.
    private static ImageTexture? _bladeTex;

    public static ImageTexture GrassBladeTex()
    {
        if (_bladeTex != null) return _bladeTex;
        const int W = 48, H = 48;
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        var r = new System.Random(4801);
        for (int b = 0; b < 6; b++)
        {
            float cx = (float)r.NextDouble() * W;
            float topFrac = 0.55f + (float)r.NextDouble() * 0.45f; // blade height as a fraction of H
            float baseW = 2.0f + (float)r.NextDouble() * 2.4f;     // half-width at the root, px
            float lean = ((float)r.NextDouble() - 0.5f) * W * 0.22f;
            int topRow = (int)((1f - topFrac) * H);
            for (int row = H - 1; row >= topRow; row--)
            {
                float up = (H - 1 - row) / (float)(H - 1);         // 0 root .. 1 image top
                float frac = Mathf.Clamp(up / topFrac, 0f, 1f);    // 0 root .. 1 this blade's tip
                float halfW = baseW * (1f - frac);
                float centre = cx + lean * frac;
                float shade = 0.60f + 0.48f * up;
                for (int col = (int)(centre - halfW); col <= (int)(centre + halfW); col++)
                {
                    if (col < 0 || col >= W) continue;
                    img.SetPixel(col, row, new Color(shade, shade, shade, 1f));
                }
            }
        }
        _bladeTex = ImageTexture.CreateFromImage(img);
        return _bladeTex;
    }

    /// <summary>
    /// V-TERRAIN: real grass. Crossed blade cards, MultiMesh-instanced only
    /// where the biome grass weight is high, thinned by a density noise, stopped
    /// at sand, rock, water and blocked cells. Three species tints jittered per
    /// instance so it is grain and colour, not flat green. Shadows off (a blade
    /// casts nothing legible at this camera) and range-culled so the field
    /// exists only when the camera is close enough to resolve it - at the
    /// maximum zoom it would be sub-pixel shimmer, which is exactly the case
    /// V3-04 range-culls the other scatter for. Seeded (2028), deterministic.
    /// Presentation only: it reads BiomeAt and the map, never the sim.
    /// </summary>
    private static void BuildGrass(Node3D parent, int w, int h,
        HashSet<(int, int)> blockedSet, IReadOnlyDictionary<(int, int), char> visual,
        float densityScale)
    {
        if (!_temperate) return; // arid maps carry dead tufts, not live grass
        const float bh = 0.40f;
        // Three crossed blade cards at 0/60/120 degrees for a fuller clump.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int p = 0; p < 3; p++)
        {
            float ang = p * Mathf.Pi / 3f;
            float dx = Mathf.Cos(ang) * 0.15f, dz = Mathf.Sin(ang) * 0.15f;
            var n = new Vector3(-Mathf.Sin(ang), 0, Mathf.Cos(ang));
            var a = new Vector3(-dx, 0, -dz);
            var b = new Vector3(dx, 0, dz);
            var c = new Vector3(dx, bh, dz);
            var d = new Vector3(-dx, bh, dz);
            st.SetNormal(n); st.SetUV(new Vector2(0, 1)); st.AddVertex(a);
            st.SetNormal(n); st.SetUV(new Vector2(1, 1)); st.AddVertex(b);
            st.SetNormal(n); st.SetUV(new Vector2(1, 0)); st.AddVertex(c);
            st.SetNormal(n); st.SetUV(new Vector2(0, 1)); st.AddVertex(a);
            st.SetNormal(n); st.SetUV(new Vector2(1, 0)); st.AddVertex(c);
            st.SetNormal(n); st.SetUV(new Vector2(0, 0)); st.AddVertex(d);
        }
        var grassMesh = st.Commit();
        var grassMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grass.gdshader") };
        grassMat.SetShaderParameter("blade_tex", GrassBladeTex());
        grassMat.SetShaderParameter("blade_height", bh);
        _windShaders.Add(grassMat);
        grassMesh.SurfaceSetMaterial(0, grassMat);

        // Three grass species, authored as the sRGB the eye should see and
        // converted to linear so they sit with the source_color ground grass.
        var tints = new[]
        {
            new Color(0.34f, 0.44f, 0.20f).SrgbToLinear(), // fresh green
            new Color(0.42f, 0.44f, 0.18f).SrgbToLinear(), // dry yellow-green
            new Color(0.24f, 0.37f, 0.16f).SrgbToLinear(), // deep green
        };

        var rng = new System.Random(2028);
        var density = _groundNoise!;
        int grassN = Scaled(6400, densityScale);
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = grassMesh,
            InstanceCount = grassN,
        };
        var zeroScale = new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero);
        for (int i = 0; i < grassN; i++)
        {
            float rx = (float)rng.NextDouble() * w, rz = (float)rng.NextDouble() * h;
            var cell = ((int)rx, (int)rz);
            var bm = BiomeAt(rx, rz);
            if (blockedSet.Contains(cell) || visual.GetValueOrDefault(cell, '.') == 'w'
                || bm.Grass < 0.42f
                || density.GetNoise2D(rx * 1.7f, rz * 1.7f) <= -0.32f)
            {
                mm.SetInstanceTransform(i, zeroScale);
                mm.SetInstanceColor(i, Colors.White);
                continue;
            }
            float sc = 0.75f + (float)rng.NextDouble() * 0.7f;
            // Grass thins toward the biome edges (weaker weight = shorter, sparser).
            sc *= Mathf.Lerp(0.6f, 1.0f, Mathf.Clamp(bm.Grass, 0f, 1f));
            var basis = Basis.FromEuler(new Vector3(0, (float)rng.NextDouble() * Mathf.Tau, 0))
                .Scaled(new Vector3(sc, sc * (0.85f + (float)rng.NextDouble() * 0.4f), sc));
            mm.SetInstanceTransform(i, new Transform3D(basis,
                new Vector3(rx, GroundHeight(rx, rz), rz)));
            var t = tints[rng.Next(tints.Length)];
            float v = 0.82f + (float)rng.NextDouble() * 0.32f;
            mm.SetInstanceColor(i, new Color(t.R * v, t.G * v, t.B * v));
        }
        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Range-cull: full grass at play height and closer, gone by the time
            // the camera is at max zoom (height 42) where a blade is sub-pixel
            // and the ground shader's own green already carries the grassland
            // read. The AABB spans the map, so this culls the whole field by
            // camera height, which is the intended "close enough to resolve it"
            // behaviour (V3-04's rationale). Measured: the fade band at CAM-B was
            // costing ~2.3 ms for grass too small to see; culling it there
            // reclaims that for the trees and rocks.
            VisibilityRangeEnd = 40f,
            VisibilityRangeEndMargin = 6f,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
            Name = "Grass",
        });
    }

    // ---- V-TERRAIN procedural mesh helpers (trees, rock formations) ----
    //
    // These build their geometry the same way every other terrain form in this
    // file does: procedural Godot primitives, not Blender assets. The Blender
    // pipeline is for the 27 UNIT models; hills, ruins, fences, cliffs and the
    // rock scatter are all procedural here, and trees and rock formations join
    // them, which keeps them inside the deterministic seeded-scatter
    // architecture and touches no .glb and no bake.

    /// <summary>Append a smooth low-poly ellipsoid blob (centre c, radii rad)
    /// to a SurfaceTool, with radial (smooth) normals so it reads as a rounded
    /// mass. Used for tree canopies and, displaced, for boulders.</summary>
    private static void AddBlob(SurfaceTool st, Vector3 c, Vector3 rad,
        int rings, int sectors, System.Func<Vector3, Vector3>? warp = null)
    {
        var p = new Vector3[rings + 1, sectors + 1];
        for (int i = 0; i <= rings; i++)
        {
            float phi = i / (float)rings * Mathf.Pi;
            for (int j = 0; j <= sectors; j++)
            {
                float th = j / (float)sectors * Mathf.Tau;
                var dir = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi),
                    Mathf.Sin(phi) * Mathf.Sin(th));
                var v = c + new Vector3(dir.X * rad.X, dir.Y * rad.Y, dir.Z * rad.Z);
                if (warp != null) v = warp(v);
                p[i, j] = v;
            }
        }
        void Tri(Vector3 a, Vector3 b, Vector3 d)
        {
            st.SetNormal((a - c).Normalized()); st.AddVertex(a);
            st.SetNormal((b - c).Normalized()); st.AddVertex(b);
            st.SetNormal((d - c).Normalized()); st.AddVertex(d);
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < sectors; j++)
            {
                Tri(p[i, j], p[i, j + 1], p[i + 1, j + 1]);
                Tri(p[i, j], p[i + 1, j + 1], p[i + 1, j]);
            }
    }

    /// <summary>Append a tapered cylinder (bark trunk) to a SurfaceTool, radial
    /// normals, from y=0 to y=height.</summary>
    private static void AddTrunk(SurfaceTool st, float r0, float r1, float height, int sides)
    {
        for (int s = 0; s < sides; s++)
        {
            float a0 = s / (float)sides * Mathf.Tau, a1 = (s + 1) / (float)sides * Mathf.Tau;
            var b0 = new Vector3(Mathf.Cos(a0) * r0, 0, Mathf.Sin(a0) * r0);
            var b1 = new Vector3(Mathf.Cos(a1) * r0, 0, Mathf.Sin(a1) * r0);
            var t0 = new Vector3(Mathf.Cos(a0) * r1, height, Mathf.Sin(a0) * r1);
            var t1 = new Vector3(Mathf.Cos(a1) * r1, height, Mathf.Sin(a1) * r1);
            var n0 = new Vector3(Mathf.Cos(a0), 0.15f, Mathf.Sin(a0)).Normalized();
            var n1 = new Vector3(Mathf.Cos(a1), 0.15f, Mathf.Sin(a1)).Normalized();
            st.SetNormal(n0); st.AddVertex(b0);
            st.SetNormal(n1); st.AddVertex(b1);
            st.SetNormal(n1); st.AddVertex(t1);
            st.SetNormal(n0); st.AddVertex(b0);
            st.SetNormal(n1); st.AddVertex(t1);
            st.SetNormal(n0); st.AddVertex(t0);
        }
    }

    /// <summary>
    /// V-TERRAIN: TREES. A procedural low-poly tree (bark trunk plus an
    /// irregular three-lobe canopy, two surfaces on one mesh), MultiMesh-
    /// instanced and CLUSTERED into copses seeded on the land cells that border
    /// water and on the map edges, so it reads as woodland along the river and
    /// the frame rather than a uniform sprinkle. Per-instance scale, yaw and
    /// canopy tint give variety from one mesh; the canopy sways in the wind.
    ///
    /// Presentation only, and clustered by design onto banks and edges so a
    /// decorative tree rarely lands on an open play cell. It never gates on the
    /// sim, so a unit may walk through a tree on open ground; the brief accepts
    /// that. It reads BiomeAt and the map, never the sim, and changes no cell's
    /// passability. Seeded (2029), deterministic.
    /// </summary>
    private static void BuildTrees(Node3D parent, int w, int h,
        HashSet<(int, int)> blockedSet, IReadOnlyDictionary<(int, int), char> visual)
    {
        // Candidate copse centres: land cells that border water, plus open cells
        // near the map edge. Both are places a copse belongs and a unit seldom
        // fights over.
        var centres = new List<(int, int)>();
        for (int cy = 0; cy < h; cy++)
            for (int cx = 0; cx < w; cx++)
            {
                if (blockedSet.Contains((cx, cy))) continue;
                bool bank = false;
                foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) })
                    if (visual.GetValueOrDefault((cx + dx, cy + dy), '.') == 'w') { bank = true; break; }
                bool edge = cx < 4 || cy < 4 || cx >= w - 4 || cy >= h - 4;
                if (bank || edge) centres.Add((cx, cy));
            }
        if (centres.Count == 0) return;

        // The tree mesh: trunk (surface 0) then canopy (surface 1).
        var stTrunk = new SurfaceTool();
        stTrunk.Begin(Mesh.PrimitiveType.Triangles);
        AddTrunk(stTrunk, 0.11f, 0.05f, 1.05f, 6);
        var treeMesh = stTrunk.Commit();

        var stLeaf = new SurfaceTool();
        stLeaf.Begin(Mesh.PrimitiveType.Triangles);
        AddBlob(stLeaf, new Vector3(0f, 1.35f, 0f), new Vector3(0.62f, 0.58f, 0.62f), 4, 6);
        AddBlob(stLeaf, new Vector3(-0.28f, 1.15f, 0.14f), new Vector3(0.46f, 0.44f, 0.46f), 3, 6);
        AddBlob(stLeaf, new Vector3(0.26f, 1.20f, -0.16f), new Vector3(0.42f, 0.42f, 0.42f), 3, 6);
        treeMesh = stLeaf.Commit(treeMesh);

        var barkMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(311, 0.5f,
                dark: new Color(0.10f, 0.075f, 0.055f),
                light: new Color(0.20f, 0.15f, 0.11f)),
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Metallic = 0f,
            Roughness = 0.95f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var canopyMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/foliage.gdshader") };
        canopyMat.SetShaderParameter("noise_tex", RawNoise(313, 0.04f));
        _windShaders.Add(canopyMat);
        treeMesh.SurfaceSetMaterial(0, barkMat);
        treeMesh.SurfaceSetMaterial(1, canopyMat);

        var canopyTints = new[]
        {
            new Color(1.02f, 1.00f, 0.86f), // warm
            new Color(0.90f, 1.00f, 0.92f), // cool
            new Color(1.00f, 0.96f, 0.78f), // dry
        };

        var rng = new System.Random(2029);
        // A set of copse centres, then trees scattered around each. Fewer, fuller
        // copses read as woodland; a flat sprinkle does not.
        int copseCount = Mathf.Clamp(centres.Count / 14, 6, 40);
        var chosen = new List<(int, int)>();
        for (int i = 0; i < copseCount; i++) chosen.Add(centres[rng.Next(centres.Count)]);

        int treeN = copseCount * 6;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = treeMesh,
            InstanceCount = treeN,
        };
        var zeroScale = new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero);
        for (int i = 0; i < treeN; i++)
        {
            var (ccx, ccy) = chosen[i / 6];
            float rx = ccx + 0.5f + (float)(rng.NextDouble() - 0.5) * 5.0f;
            float rz = ccy + 0.5f + (float)(rng.NextDouble() - 0.5) * 5.0f;
            var cell = ((int)rx, (int)rz);
            if (rx < 0 || rz < 0 || rx >= w || rz >= h
                || blockedSet.Contains(cell) || visual.GetValueOrDefault(cell, '.') == 'w')
            {
                mm.SetInstanceTransform(i, zeroScale);
                mm.SetInstanceColor(i, Colors.White);
                continue;
            }
            float sc = 0.8f + (float)rng.NextDouble() * 0.7f;
            var basis = Basis.FromEuler(new Vector3(0, (float)rng.NextDouble() * Mathf.Tau, 0))
                .Scaled(new Vector3(sc, sc * (0.9f + (float)rng.NextDouble() * 0.35f), sc));
            mm.SetInstanceTransform(i, new Transform3D(basis,
                new Vector3(rx, GroundHeight(rx, rz) - 0.05f, rz)));
            var t = canopyTints[rng.Next(canopyTints.Length)];
            float v = 0.82f + (float)rng.NextDouble() * 0.3f;
            mm.SetInstanceColor(i, new Color(t.R * v, t.G * v, t.B * v));
        }
        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            // Shadows OFF. Measured: with the double-sided canopies casting into
            // the 4096 atlas, CAM-B (whole map, every copse in the shadow
            // cascade at once) collapsed to about 1.5 fps - the exact "cost more
            // than they show" case the brief names. At RTS distance the tree's
            // own form and the SSAO contact darkening carry it; a per-tree cast
            // shadow over the whole map is not the depth cue that survives here.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Name = "Trees",
        });
    }

    /// <summary>
    /// V-TERRAIN: ROCK FORMATIONS. Larger irregular boulders (a low-poly blob
    /// displaced by a deterministic per-vertex warp for a real lumpy silhouette),
    /// MultiMesh-instanced on the rocky biome, the feet of hills/ruins/ridges,
    /// and a few at the water banks so a boulder reads at the shore. Distinct
    /// from the sub-pixel debris scatter (BuildScatter's "Rocks"): these have
    /// silhouette. The material is noise-driven grey-brown albedo AND roughness
    /// with a detail normal and, per the V2 lesson, metallic 0 (no constant
    /// metallic, no chalky specular). Seeded (2030), deterministic, presentation
    /// only; it reads BiomeAt and the map, never the sim.
    /// </summary>
    private static void BuildRockFormations(Node3D parent, int w, int h,
        HashSet<(int, int)> blockedSet, IReadOnlyDictionary<(int, int), char> visual,
        float densityScale)
    {
        // One lumpy boulder mesh, reused; per-instance scale and yaw vary it.
        var warpNoise = new FastNoiseLite
        {
            Seed = 5150, Frequency = 1.4f, FractalOctaves = 2,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBlob(st, Vector3.Zero, new Vector3(0.55f, 0.42f, 0.5f), 4, 7, v =>
        {
            float d = 1f + warpNoise.GetNoise3D(v.X * 3f, v.Y * 3f, v.Z * 3f) * 0.35f;
            return new Vector3(v.X * d, Mathf.Max(v.Y * d, -0.02f), v.Z * d);
        });
        var rockMesh = st.Commit();
        rockMesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            // Grey-brown, light enough to catch the warm key and read as lit
            // rock rather than a black lump (the first capture baked too dark).
            AlbedoTexture = GrainTex(519, 0.09f,
                dark: new Color(0.19f, 0.175f, 0.155f),
                light: new Color(0.42f, 0.385f, 0.335f)),
            Uv1Triplanar = true, Uv1WorldTriplanar = true,
            NormalEnabled = true,
            NormalTexture = GrainTex(521, 0.14f, normalMap: true),
            NormalScale = 1.0f,
            RoughnessTexture = GrainTex(523, 0.18f), // noise-driven roughness
            Roughness = 1.0f,
            Metallic = 0f,
            VertexColorUseAsAlbedo = true,
        });

        // Candidate cells: rocky-biome open cells, cliff-foot boundaries, and
        // water banks (for a shore boulder).
        var rocky = new List<(int, int)>();
        var banks = new List<(int, int)>();
        for (int cy = 0; cy < h; cy++)
            for (int cx = 0; cx < w; cx++)
            {
                if (blockedSet.Contains((cx, cy))) continue;
                if (BiomeAt(cx, cy).Rock > 0.5f) rocky.Add((cx, cy));
                foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (visual.GetValueOrDefault((cx + dx, cy + dy), '.') == 'w') { banks.Add((cx, cy)); break; }
            }
        var feet = new List<(int, int)>();
        foreach (var (cx, cy) in blockedSet)
        {
            char k = visual.GetValueOrDefault((cx, cy), '#');
            if (k is 'w' or 'f') continue;
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                if (!blockedSet.Contains((cx + dx, cy + dy))) { feet.Add((cx + dx, cy + dy)); break; }
        }

        var rng = new System.Random(2030);
        int rockN = Scaled(220, densityScale);
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = rockMesh,
            InstanceCount = rockN,
        };
        var zeroScale = new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero);
        for (int i = 0; i < rockN; i++)
        {
            (int, int)? src;
            double pick = rng.NextDouble();
            if (pick < 0.42 && feet.Count > 0) src = feet[rng.Next(feet.Count)];
            else if (pick < 0.72 && rocky.Count > 0) src = rocky[rng.Next(rocky.Count)];
            else if (banks.Count > 0) src = banks[rng.Next(banks.Count)];
            else if (rocky.Count > 0) src = rocky[rng.Next(rocky.Count)];
            else src = null;
            if (src == null) { mm.SetInstanceTransform(i, zeroScale); mm.SetInstanceColor(i, Colors.White); continue; }
            var (scx, scy) = src.Value;
            float rx = scx + 0.5f + (float)(rng.NextDouble() - 0.5) * 1.8f;
            float rz = scy + 0.5f + (float)(rng.NextDouble() - 0.5) * 1.8f;
            var cell = ((int)rx, (int)rz);
            if (rx < 0 || rz < 0 || rx >= w || rz >= h
                || blockedSet.Contains(cell) || visual.GetValueOrDefault(cell, '.') == 'w')
            {
                mm.SetInstanceTransform(i, zeroScale);
                mm.SetInstanceColor(i, Colors.White);
                continue;
            }
            float sc = 0.5f + (float)rng.NextDouble() * 0.9f;
            var basis = Basis.FromEuler(new Vector3(
                ((float)rng.NextDouble() - 0.5f) * 0.3f,
                (float)rng.NextDouble() * Mathf.Tau,
                ((float)rng.NextDouble() - 0.5f) * 0.3f))
                .Scaled(new Vector3(sc * (0.8f + (float)rng.NextDouble() * 0.6f), sc, sc));
            mm.SetInstanceTransform(i, new Transform3D(basis,
                new Vector3(rx, GroundHeight(rx, rz) + sc * 0.12f, rz)));
            float shv = 0.82f + (float)rng.NextDouble() * 0.28f;
            mm.SetInstanceColor(i, new Color(shv, shv * 0.98f, shv * 0.95f));
        }
        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            // Shadows OFF for the same reason as the trees and the rest of the
            // scatter: a few hundred boulders casting into the atlas over the
            // whole map at max zoom costs more than it shows. SSAO grounds them.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Name = "RockFormations",
        });
    }

    /// <summary>
    /// TICKET-P5-DEF-01: a flat weapon-range ring. Built at the radius passed
    /// in, but callers hand it 1.0 and drive the radius through Scale, so one
    /// mesh serves every range and the per-placement allocation stays a node
    /// rather than a mesh rebuild. Unshaded and alpha-blended so the ring reads
    /// as a HUD overlay lying on the ground rather than as lit geometry.
    /// </summary>
    public static MeshInstance3D MakeRangeRing(float radius, Color c) => new()
    {
        Mesh = new TorusMesh { InnerRadius = radius - 0.08f, OuterRadius = radius },
        Position = new Vector3(0, 0.04f, 0),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = c,
            EmissionEnabled = true,
            Emission = c,
            EmissionEnergyMultiplier = 1.1f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        },
        Name = "RangeRing",
    };

    /// <summary>Ferrite gold for the ghost's own range ring (DEF-01), and a
    /// dimmer pour for the rings of structures already standing.</summary>
    public static readonly Color RangeRingOwn = new(0.79f, 0.63f, 0.36f, 0.35f);
    public static readonly Color RangeRingCoverage = new(0.79f, 0.63f, 0.36f, 0.18f);

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
