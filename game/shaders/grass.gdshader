shader_type spatial;
// V-TERRAIN. Real grass, not a green tint on the ground. Crossed blade cards,
// MultiMesh-instanced only where the biome grass weight is high, alpha-cut to a
// ragged blade silhouette, tinted per instance to three grass species so a
// field is grain and colour rather than flat green.
//
// The wind sway is a vertex offset weighted by blade height (the tips move, the
// roots do not), driven by wind_phase. wind_phase is advanced by
// BattlefieldView.TickWater from _Process, NOT shader TIME, so it freezes when
// the look-dev harness pauses the tree and two captures stay byte-identical.
// (doc 25 blesses "a vertex shader offset by TIME" as presentation-only and
// legal; a _Process uniform is the same thing but harness-deterministic.)
render_mode cull_disabled, depth_prepass_alpha;

uniform sampler2D blade_tex : source_color, filter_linear_mipmap;
uniform float wind_phase = 0.0;
uniform float wind_strength = 0.075;
uniform float blade_height = 0.34;
uniform float alpha_scissor = 0.4;

varying vec3 inst_col;

void vertex() {
    inst_col = COLOR.rgb; // MultiMesh per-instance colour (use_colors = true)
    vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float h = clamp(VERTEX.y / blade_height, 0.0, 1.0);
    float sway = sin(wind_phase * 1.3 + wp.x * 0.6 + wp.z * 0.5) * 0.7
               + sin(wind_phase * 2.1 + wp.x * 1.3) * 0.3;
    VERTEX.x += sway * wind_strength * h;
    VERTEX.z += sin(wind_phase * 1.1 + wp.z * 0.7) * wind_strength * 0.5 * h;
}

void fragment() {
    vec4 t = texture(blade_tex, UV);
    if (t.a < alpha_scissor) discard;
    // t.rgb is a vertical shade (darker at the root) baked into the card.
    ALBEDO = inst_col * t.rgb;
    ROUGHNESS = 0.95;
    SPECULAR = 0.1;
}
