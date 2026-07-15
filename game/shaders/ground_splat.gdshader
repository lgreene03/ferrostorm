shader_type spatial;
uniform sampler2D noise_a : repeat_enable, filter_linear_mipmap;
uniform sampler2D noise_b : repeat_enable, filter_linear_mipmap;
uniform sampler2D detail_normal : hint_normal, repeat_enable, filter_linear_mipmap;
uniform vec3 dust_dark : source_color = vec3(0.085, 0.09, 0.10);
uniform vec3 dust_light : source_color = vec3(0.145, 0.15, 0.165);
uniform vec3 rock_dark : source_color = vec3(0.105, 0.10, 0.095);
uniform vec3 rock_light : source_color = vec3(0.185, 0.175, 0.16);
uniform vec3 gravel_dark : source_color = vec3(0.06, 0.065, 0.075);
uniform vec3 gravel_light : source_color = vec3(0.125, 0.135, 0.15);
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    float m1 = texture(noise_a, wpos.xz * 0.020).r;
    float m2 = texture(noise_b, wpos.xz * 0.007).r;
    float g1 = texture(noise_a, wpos.xz * 0.11).r;
    float g2 = texture(noise_b, wpos.xz * 0.23).r;
    vec3 dust = mix(dust_dark, dust_light, g1);
    vec3 rock = mix(rock_dark, rock_light, g2);
    vec3 gravel = mix(gravel_dark, gravel_light, 0.5 * (g1 + g2));
    float w_rock = smoothstep(0.45, 0.62, m1);
    float w_gravel = smoothstep(0.55, 0.72, m2) * (1.0 - w_rock);
    vec3 alb = mix(dust, rock, w_rock);
    alb = mix(alb, gravel, w_gravel);
    alb *= mix(0.85, 1.12, texture(noise_b, wpos.xz * 0.003).r);
    ALBEDO = alb;
    ROUGHNESS = 0.96 - 0.05 * w_gravel;
    NORMAL_MAP = texture(detail_normal, wpos.xz * 0.15).rgb;
    NORMAL_MAP_DEPTH = 0.55 + 0.4 * w_rock;
}
