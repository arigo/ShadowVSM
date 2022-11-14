
float4 vsm_depth_frag(float4 sv_position)
{
    float depth = sv_position.z;
#ifdef UNITY_REVERSED_Z
    depth = 0.5 - depth;
#else
    depth = depth - 0.5;
#endif
    depth *= 128;

    float dx = ddx(depth);
    float dy = ddy(depth);
    float bias = 0.25 * (dx * dx + dy * dy);

    return float4(depth, depth * depth + bias, 1, 0);
}
