shader_type spatial;
// V-TERRAIN (doc 25 wave, subsuming doc 22 Wave A.5). This replaces
// ground_splat.gdshader. The old shader painted ONE arid material everywhere:
// dust/rock/gravel, all dark grey-brown, no grass, no sand, no biome. The
// owner's complaint was that the ground looks flat and untextured, and it did:
// three near-identical grey-browns blended by noise is a monochrome frame.
//
// This shader blends three real biomes - grass, sand, rock - by a CPU-built
// control map (BattlefieldView.BuildBiomeMap), and gives each real spatial
// variation: multi-octave noise on ALBEDO and ROUGHNESS, a detail normal, and
// a large-scale hue drift so no surface is a flat colour.
//
// EVERYTHING IS WORLD-SPACE, deliberately. The old shader sampled its splat by
// wpos.xz so the "dust-capped mesa" thin slabs (BattlefieldView, the cliff
// pass) picked up the same pattern as the ground below them through the
// MultiMesh MODEL_MATRIX. The biome control map is therefore ALSO sampled by
// world position (wpos.xz / map_size), not by UV: a per-face BoxMesh UV would
// smear the whole biome gradient across every tiny cap. Keep it world-space.
//
// The three samplers keep filter_linear_mipmap_anisotropic (V1-04): at a fixed
// -50 pitch the ground is always at a grazing angle and always most of the
// frame, which is the exact case anisotropic filtering exists for, and the
// project-wide key does not reach a shader.
uniform sampler2D noise_a : repeat_enable, filter_linear_mipmap_anisotropic;
uniform sampler2D noise_b : repeat_enable, filter_linear_mipmap_anisotropic;
uniform sampler2D detail_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
// The biome control map: R grass, G sand, B rock (blend weights), A wetness
// (shoreline damp band). Linear so cell-to-cell biome transitions are smooth,
// clamped so sampling just past the map edge holds the edge biome.
uniform sampler2D biome_map : filter_linear, repeat_disable;
uniform vec2 map_size = vec2(96.0, 64.0);

// Muted, storm-world biomes (doc 16 is an ashen/cinder palette; grass here is
// a real but weathered olive-green, not lush emerald, so it reads GREEN against
// the sand and rock without fighting the mood). source_color: authored as the
// sRGB the eye should see, converted to linear by Godot.
uniform vec3 grass_dark : source_color = vec3(0.075, 0.115, 0.050);
uniform vec3 grass_light : source_color = vec3(0.300, 0.400, 0.180);
uniform vec3 grass_dry : source_color = vec3(0.380, 0.400, 0.170);
uniform vec3 sand_dark : source_color = vec3(0.280, 0.230, 0.150);
uniform vec3 sand_light : source_color = vec3(0.580, 0.500, 0.340);
uniform vec3 rock_dark : source_color = vec3(0.095, 0.095, 0.105);
uniform vec3 rock_light : source_color = vec3(0.270, 0.255, 0.235);
// What wet ground multiplies toward: darker and slightly cooler (damp sand).
uniform vec3 wet_tint : source_color = vec3(0.45, 0.42, 0.40);

varying vec3 wpos;

void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    // Biome weights, sampled in world space and renormalised so any authoring
    // slack in the control map cannot brighten or darken the sum.
    vec2 buv = wpos.xz / map_size;
    vec4 biome = texture(biome_map, buv);
    float wg = biome.r, ws = biome.g, wr = biome.b;
    float sum = wg + ws + wr + 1e-4;
    wg /= sum; ws /= sum; wr /= sum;
    float wet = biome.a;

    // Multi-octave world-space noise. macro = large patches, mid = medium
    // blotching, micro = grain, fine = the last octave that keeps a close
    // surface from going flat. This is the "proper texture": real variation at
    // several frequencies rather than one soft blob across the whole map.
    float macro = texture(noise_b, wpos.xz * 0.013).r;
    float mid   = texture(noise_a, wpos.xz * 0.060).r;
    float micro = texture(noise_a, wpos.xz * 0.230).r;
    float fine  = texture(noise_b, wpos.xz * 0.520).r;

    // GRASS: green, with a value range from the mid/micro octaves and a
    // large-scale hue DRIFT between blue-green and dry yellow-green so a field
    // is never one flat green. Fine octave breaks up the last of the flatness.
    vec3 grass = mix(grass_dark, grass_light, clamp(mid * 0.65 + micro * 0.35, 0.0, 1.0));
    grass = mix(grass, grass_dry, smoothstep(0.52, 0.82, macro));
    grass *= (0.82 + 0.34 * fine);

    // SAND: warm tan on a FINER grain than rock (its own high-frequency octave),
    // so a beach reads as sand and not as tinted rock.
    vec3 sand = mix(sand_dark, sand_light, clamp(micro * 0.55 + fine * 0.45, 0.0, 1.0));
    sand *= (0.88 + 0.24 * texture(noise_a, wpos.xz * 0.85).r);

    // ROCK: cool grey-brown with a strata-like low-frequency banding.
    vec3 rock = mix(rock_dark, rock_light, clamp(mid * 0.5 + macro * 0.5, 0.0, 1.0));
    rock *= (0.82 + 0.34 * micro);

    vec3 alb = grass * wg + sand * ws + rock * wr;
    // Wetness: damp the shoreline toward the wet tint (darker, cooler).
    alb = mix(alb, alb * wet_tint, wet * 0.82);
    ALBEDO = alb;

    // ROUGHNESS per biome, noise-modulated (proper texture on the spec channel
    // too, not just albedo), and dropped where wet so the damp band catches a
    // sheen. grass 0.90, sand 1.0, rock 0.82.
    float rough = 0.90 * wg + 1.00 * ws + 0.82 * wr;
    rough += 0.14 * (micro - 0.5);
    rough = mix(rough, 0.42, wet * 0.7);
    ROUGHNESS = clamp(rough, 0.30, 1.0);

    // Detail normal, faded with distance (V3-03's lesson: an undersampled
    // detail normal shimmers as the camera pans, and that shimmer is the single
    // largest "looks cheap in motion" contributor). Deeper on rock/sand, shallow
    // on grass (whose relief comes from the grass blades, not the ground map).
    float ndfade = 1.0 - smoothstep(30.0, 90.0, length(VERTEX));
    NORMAL_MAP = texture(detail_normal, wpos.xz * 0.06).rgb;
    NORMAL_MAP_DEPTH = (0.35 + 0.55 * wr + 0.35 * ws) * ndfade;
}
