shader_type spatial;
// V-TERRAIN. Tree canopies. Double-sided (a low-poly leaf mass reads better
// with both faces lit; Godot flips the normal for back faces so the shading
// stays correct), noise-varied albedo and roughness so a canopy is not a flat
// green blob, per-instance tint from the MultiMesh instance colour, and a gentle
// height-weighted wind sway sharing the wind_phase TickWater advances from
// _Process (frozen under the harness pause, so captures stay byte-identical).
render_mode cull_disabled;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 leaf_dark : source_color = vec3(0.11, 0.19, 0.075);
uniform vec3 leaf_light : source_color = vec3(0.28, 0.38, 0.16);
uniform float wind_phase = 0.0;
uniform float wind_strength = 0.06;
uniform float sway_base = 0.85; // local y above which the canopy starts to sway

varying vec3 icol;
varying vec3 wpos;

void vertex() {
    icol = COLOR.rgb;
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float hw = clamp((VERTEX.y - sway_base) / 1.3, 0.0, 1.0);
    VERTEX.x += sin(wind_phase * 1.0 + wpos.x * 0.4 + wpos.z * 0.35) * wind_strength * hw;
    VERTEX.z += cos(wind_phase * 0.8 + wpos.z * 0.4) * wind_strength * 0.7 * hw;
}

void fragment() {
    float n = texture(noise_tex, wpos.xz * 0.6 + vec2(wpos.y * 0.3)).r;
    float n2 = texture(noise_tex, wpos.xz * 1.9).r;
    vec3 leaf = mix(leaf_dark, leaf_light, clamp(n * 0.7 + n2 * 0.3, 0.0, 1.0));
    ALBEDO = leaf * icol;
    ROUGHNESS = clamp(0.85 + 0.12 * (n2 - 0.5), 0.6, 1.0);
    SPECULAR = 0.15;
}
