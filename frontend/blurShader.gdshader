shader_type canvas_item;
render_mode unshaded;

uniform float radius_px = 8.0; // blur radius in pixels per tap
uniform int samples = 12;       // taps to each side
uniform sampler2D SCREEN_TEXTURE : hint_screen_texture;

//void fragment(){ COLOR = vec4(1.0, 0.0, 0.0, 0.5	); }
void fragment() {
    vec2 uv = SCREEN_UV;
    vec2 px = SCREEN_PIXEL_SIZE;

    vec4 acc = texture(SCREEN_TEXTURE, uv);
    float wsum = 1.0;

    for (int i = 1; i <= samples; i++) {
        float w = 1.0 - float(i) / float(samples + 1);
        vec2 d = float(i) * radius_px * px;

        acc += texture(SCREEN_TEXTURE, uv + vec2( d.x, 0.0)) * w;
        acc += texture(SCREEN_TEXTURE, uv + vec2(-d.x, 0.0)) * w;
        acc += texture(SCREEN_TEXTURE, uv + vec2(0.0,  d.y)) * w;
        acc += texture(SCREEN_TEXTURE, uv + vec2(0.0, -d.y)) * w;

        wsum += 4.0 * w;
    }

    COLOR = acc / wsum;
}
