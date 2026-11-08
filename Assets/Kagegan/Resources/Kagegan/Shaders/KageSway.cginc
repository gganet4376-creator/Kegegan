// Hair and cloth sway for the procedural fighters.
// The mesh stores a sway weight in uv2.x (uv2.y = 7 marks it); HumanoidRig sets _KageSway per fighter:
// xyz = world offset of a fully free strand (trails the body's motion), w = flutter amplitude in metres.
#ifndef KAGE_SWAY_INCLUDED
#define KAGE_SWAY_INCLUDED
float4 _KageSway;
float3 KageSway(float3 wpos, float2 sw)
{
    float w = sw.y > 6.5 ? sw.x : 0.0;
    float t = _Time.y;
    float3 flutter = float3(sin(t * 5.3 + wpos.y * 9.0), sin(t * 4.1 + wpos.x * 7.0) * 0.4, cos(t * 4.7 + wpos.y * 8.0 + wpos.z * 5.0));
    return wpos + (_KageSway.xyz + flutter * _KageSway.w) * w;
}
#endif
