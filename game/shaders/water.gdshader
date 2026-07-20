shader_type spatial;
// V-TERRAIN. The river was a dark, near-black opaque slab (StandardMaterial3D,
// metallic 0.85, roughness 0.08) that scrolled a normal map. From the RTS
// camera it read as a hole, not as water. This is a real water surface:
//
//   * DEPTH colour, read from the scene depth behind the surface, so it is a
//     light shallow teal where it meets the bank and a deep blue-green in the
//     channel. The basin is shallow (the geometry is unchanged, per the brief),
//     so depth_scale stretches that thin column into a full shallow-to-deep
//     ramp - the gradient reads as depth even though the water is not deep.
//   * A FRESNEL brightening at grazing angles (an RTS view is oblique), which
//     lifts the far side and lets the shallow edge thin out over the wet sand.
//   * Two scrolling ripple NORMALS, world-space so the per-cell slabs share one
//     continuous moving surface with no seam, giving the sun a moving glint.
//   * SHORE FOAM where the water is shallow against the land, broken up and
//     scrolled by noise so it reads as a waterline, not a drawn outline.
//
// Transparent queue so DEPTH_TEXTURE gives the opaque scene depth behind the
// water. NOT depth_draw_opaque: the basin is tiled from per-cell slabs scaled
// 1.02 so neighbours overlap by 0.02, and writing depth from coplanar
// overlapping transparent slabs z-fights into a grid of dark seams across the
// river (measured, it is the banding the first water capture showed). Leaving
// depth-write off lets the tiles blend cleanly; opaque geometry (the bridge,
// units) still occludes the water through the normal depth test. wave_time is a
// _Process-advanced uniform (see BattlefieldView.TickWater), NOT shader TIME,
// so it freezes when the look-dev harness pauses the tree and two captures stay
// byte-identical.
render_mode blend_mix, cull_back, specular_schlick_ggx;

uniform sampler2D ripple_normal : hint_normal, repeat_enable, filter_linear_mipmap;
uniform sampler2D depth_tex : hint_depth_texture, filter_linear;
uniform vec3 shallow_color : source_color = vec3(0.24, 0.50, 0.50);
uniform vec3 deep_color : source_color = vec3(0.035, 0.175, 0.240);
uniform vec3 foam_color : source_color = vec3(0.82, 0.90, 0.92);
uniform float wave_time = 0.0;
// The basin is a shallow ~0.15 m column (the brief forbids rebuilding it), so
// depth_scale maps almost all of it to "deep" and only the last few centimetres
// at the bank ramp to "shallow"; foam_width keeps the foam a thin shoreline
// rather than a frost over the whole river.
uniform float depth_scale = 8.0;
uniform float foam_width = 0.05;

varying vec3 wpos;

void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    // Two scrolling ripple normals in world space (no per-cell seam).
    vec2 uv1 = wpos.xz * 0.35 + vec2(0.030, 0.020) * wave_time;
    vec2 uv2 = wpos.xz * 0.17 + vec2(-0.022, 0.034) * wave_time;
    vec3 n1 = texture(ripple_normal, uv1).xyz * 2.0 - 1.0;
    vec3 n2 = texture(ripple_normal, uv2).xyz * 2.0 - 1.0;
    vec3 rn = normalize(n1 + n2);
    NORMAL_MAP = rn * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 0.55;

    // Water depth: reconstruct the view-space distance to the opaque geometry
    // behind this fragment and subtract the surface's own distance.
    float raw = texture(depth_tex, SCREEN_UV).r;
    vec4 vp = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
    vp.xyz /= vp.w;
    float sceneDist = -vp.z;
    float fragDist = -VERTEX.z;
    float waterDepth = max(sceneDist - fragDist, 0.0);
    float depthT = clamp(waterDepth * depth_scale, 0.0, 1.0);

    vec3 col = mix(shallow_color, deep_color, depthT);

    // Shore foam: shallow water only, broken and scrolled so it is a waterline.
    float foam = 1.0 - smoothstep(0.0, foam_width, waterDepth);
    float fn = texture(ripple_normal, wpos.xz * 0.55 + vec2(0.05, 0.0) * wave_time).x;
    foam *= smoothstep(0.30, 0.72, fn);
    col = mix(col, foam_color, foam);

    ALBEDO = col;
    METALLIC = 0.0;
    ROUGHNESS = mix(0.05, 0.14, depthT);

    // Fresnel from the geometric normal: more opaque head-on and in the deep,
    // thinner at the shallow grazing edge so the damp bank shows through.
    float fres = pow(1.0 - clamp(dot(NORMAL, normalize(-VERTEX)), 0.0, 1.0), 3.0);
    float a = clamp(0.60 + 0.35 * depthT + 0.35 * fres, 0.0, 1.0);
    ALPHA = max(a, foam);
}
