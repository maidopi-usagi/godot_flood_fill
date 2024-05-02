#[compute]
#version 450

#define OP_NONE 0
#define OP_CLICK 1
#define OP_CLEAR 2
#define OP_PREPASS_LINE 4
#define OP_PREPASS_COLOR 8
#define OP_SEEDING_MASK_BUFFER 16
#define OP_FLOODING_MASK_BUFFER 32

#define SHOW_MASK 0
#define SHOW_ORIGINAL 1
#define SHOW_MATTED 2

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba32f, set = 0, binding = 0) uniform restrict writeonly image2D output_image;
layout(rgba32f, set = 1, binding = 0) uniform restrict readonly image2D prev_image;
layout(set = 2, binding = 0) uniform sampler2D base_image;
layout(r8, set = 3, binding = 0) uniform restrict writeonly image2D mask_image;
layout(rgba8, set = 4, binding = 0) uniform restrict writeonly image2D display_image;

layout(push_constant, std430) uniform Param
{
    ivec2 texture_size;
    ivec2 query_pos;
    int op_flag;
    int display_mode;
    int pass_counter;
    float threshold;
    vec4 color_target;
} params;

#define MOD3 vec3(.1031,.11369,.13787)
vec3 hash31(float p) {
    vec3 p3 = fract(vec3(p) * MOD3);
    p3 += dot(p3, p3.yzx + 19.19);
    return fract(vec3((p3.x + p3.y)*p3.z, (p3.x+p3.z)*p3.y, (p3.y+p3.z)*p3.x));
}

float hash11(vec2 p) { return fract(1e4 * sin(17.0 * p.x + p.y * 0.1) * (0.1 + abs(sin(p.y * 13.0 + p.x)))); }

void main() {
    ivec2 st = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = vec2(st) / params.texture_size;

    vec4 prev_data = imageLoad(prev_image, st);
    vec4 mouse_data = imageLoad(prev_image, params.query_pos);
    vec4 mask_color = texture(base_image, uv);
    
    bool mouse_mask = mouse_data.g == prev_data.g && mouse_data.g > 0.0;

    if (params.display_mode == SHOW_MASK)
    { imageStore(display_image, st, vec4(hash31(prev_data.g * 456.48) + (mouse_mask ? vec3(0.25) : vec3(0.0)), 1.0)); }
    else if (params.display_mode == SHOW_ORIGINAL)
    { imageStore(display_image, st, mask_color); }
    else if (params.display_mode == SHOW_MATTED)
    { imageStore(display_image, st, mouse_mask ? mask_color : vec4(mask_color.rgb, 0.1f)); }
    
    
    if (params.op_flag == OP_NONE)
    {
        return;
    }
    
    if (params.op_flag == OP_CLICK)
    {
        imageStore(mask_image, st, mouse_mask ? vec4(1.0) : vec4(0.0));
        return;
    }
    
    if (params.op_flag == OP_PREPASS_LINE)
    { 
        mask_color.rgb = vec3(0.21 * mask_color.r + 0.71 * mask_color.g + 0.07 * mask_color.b);
        imageStore(output_image, st, step(params.threshold, mask_color)); 
    }
    else if (params.op_flag == OP_PREPASS_COLOR)
    {
        imageStore(output_image, st, step(params.threshold, vec4(distance(mask_color.rgb, params.color_target.rgb))));
    }
    else if (params.op_flag == OP_SEEDING_MASK_BUFFER)
    {
        float c00 = imageLoad(prev_image, st).g;
        float c10 = imageLoad(prev_image, st - ivec2(1, 0)).g;
        float c01 = imageLoad(prev_image, st - ivec2(0, 1)).g;
        if (c00 >= 1.0 && c01 == 0.0 && c10 == 0.0)
        {
            c00 = fract(float(st.x) * 0.000145 + float(st.y) * 0.0032) * 0.521 + 0.263;
        }
        imageStore(output_image, st, vec4(c00,c00,c00,1.0));
    }
    else if (params.op_flag == OP_FLOODING_MASK_BUFFER)
    {
        float c00 = prev_data.g;
        if (c00 > 0.0)
        {
            int horzFillDist = (params.pass_counter & 1) == 0 ? 256 : 0;
            int vertFillDist = (params.pass_counter & 1) == 1 ? 256 : 0;
            for (int i = 1; i < horzFillDist; i++)
            {
                float p = imageLoad(prev_image, st + ivec2(i, 0)).g;
                if (p == 0.0) break;
                if (p < c00) c00 = p;
            }
            for (int i = 1; i < horzFillDist; i++)
            {
                float p = imageLoad(prev_image, st - ivec2(i, 0)).g;
                if (p == 0.0) break;
                if (p < c00) c00 = p;
            }
            for (int i = 1; i < vertFillDist; i++)
            {
                float p = imageLoad(prev_image, st + ivec2(0, i)).g;
                if (p == 0.0) break;
                if (p < c00) c00 = p;
            }
            for (int i = 1; i < vertFillDist; i++)
            {
                float p = imageLoad(prev_image, st - ivec2(0, i)).g;
                if (p == 0.0) break;
                if (p < c00) c00 = p;
            }
        }
        imageStore(output_image, st, vec4(c00,c00,c00,1.0));
    }
}
