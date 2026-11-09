// Real sun shadow maps, rendered by KageShadowMap.cs and read by the toon and surface shaders.
// Two orthographic depth maps seen from the sun:
//   dynamic: the fighters only, re-rendered every frame in a tight box around them (sharp contact and self-shadows);
//   static:  trees, rocks, buildings and props of the arena, rendered once (again only when something breaks).
// Each map stores the light-space depth d (0 = nearest to the sun) of the closest caster in R (or packed into RG
// when half-float targets are missing). A receiver is lit when its own d is not behind the stored one.
#ifndef KAGE_SHADOW_MAP
#define KAGE_SHADOW_MAP

sampler2D _KageSMDyn;
sampler2D _KageSMStat;
float4x4 _KageSMDynM;      // world -> (u, v, d), all 0..1
float4x4 _KageSMStatM;
float4 _KageSMDynP;        // x = texel size in uv, y = depth bias, z = normal offset (m), w = filter (0 off, 1 point, 2 bilinear PCF, 3 4x4 PCF)
float4 _KageSMStatP;
float4 _KageSMFade;        // x = camera distance where shadows start fading, y = where they are gone, z = strength
float _KageSMOn;           // 1 = shadow maps drive the sun shadows (0 = older capsule / baked fallback)
float _KageSMPack;         // 1 = depth packed into RG (ARGB32 targets)
float4x4 _KageSMCur;       // matrix of the map being rendered (caster pass)

inline float KageSMRead(sampler2D t, float2 uv)
{
    float4 s = tex2Dlod(t, float4(uv, 0, 0));
    return _KageSMPack > 0.5 ? DecodeFloatRG(s.rg) : s.r;
}

// 1 when the receiver depth z is in front of the stored caster (or nothing was drawn there: cleared to 1)
inline half KageSMTest(sampler2D t, float2 uv, float z)
{
    float s = KageSMRead(t, uv);
    return (half)max(step(z, s), step(0.9995, s));
}

// 2x2 taps weighted like bilinear filtering: smooth edges at the cost of four reads
inline half KageSMBilinear(sampler2D t, float3 c, float texel)
{
    float2 st = c.xy / texel - 0.5;
    float2 f = frac(st);
    float2 b = (floor(st) + 0.5) * texel;
    half s00 = KageSMTest(t, b, c.z);
    half s10 = KageSMTest(t, b + float2(texel, 0), c.z);
    half s01 = KageSMTest(t, b + float2(0, texel), c.z);
    half s11 = KageSMTest(t, b + float2(texel, texel), c.z);
    return lerp(lerp(s00, s10, f.x), lerp(s01, s11, f.x), f.y);
}

half KageSMSample(sampler2D t, float4x4 m, float4 p, float3 wpos, half3 wn, half3 l)
{
    if (p.w <= 0) return 1.0;
    // normal offset: push the lookup off the surface, more where the sun grazes it (kills acne without peter-panning)
    half ndl = dot(wn, l);
    half slope = 1.0 - saturate(abs(ndl));
    float3 q = wpos + wn * (p.z * (0.4 + slope));
    float3 c = mul(m, float4(q, 1.0)).xyz;
    // fade out over the outer 6% of the map instead of a hard edge
    float2 e = saturate(min(c.xy, 1.0 - c.xy) * 16.0);
    half edge = e.x * e.y;
    if (edge <= 0) return 1.0;
    c.z -= p.y * (1.0 + slope * 2.0);
    half s;
    if (p.w < 1.5) s = KageSMTest(t, c.xy, c.z);
    else if (p.w < 2.5) s = KageSMBilinear(t, c, p.x);
    else
    {
        // 4x4 footprint as four bilinear groups: wide, soft penumbra (Ultra)
        float o = p.x;
        s = (KageSMBilinear(t, c + float3(-o, -o, 0), p.x) + KageSMBilinear(t, c + float3(o, -o, 0), p.x)
           + KageSMBilinear(t, c + float3(-o, o, 0), p.x) + KageSMBilinear(t, c + float3(o, o, 0), p.x)) * 0.25;
    }
    return lerp(1.0, s, edge);
}

// Sun visibility 0..1 at a world position (wn = geometric world normal, l = direction to the sun).
half KageShadowMap(float3 wpos, half3 wn, half3 l)
{
    half s = KageSMSample(_KageSMStat, _KageSMStatM, _KageSMStatP, wpos, wn, l);
    s = min(s, KageSMSample(_KageSMDyn, _KageSMDynM, _KageSMDynP, wpos, wn, l));
    float dist = length(wpos - _WorldSpaceCameraPos.xyz);
    half far = saturate((dist - _KageSMFade.x) / max(0.01, _KageSMFade.y - _KageSMFade.x));
    return lerp(1.0, s, _KageSMFade.z * (1.0 - far));
}

// Shared caster pass body. The including pass defines KAGE_SM_WPOS(v) (world position incl. wind / sway)
// and KAGE_SM_CLIP(i) (alpha cut-out test, may be empty).
struct v2fKageSM
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float d : TEXCOORD1;
};

inline float4 KageSMOut(float d)
{
    d = clamp(d, 0.0, 0.999);
    return _KageSMPack > 0.5 ? float4(EncodeFloatRG(d), 0, 1) : float4(d, d, d, 1);
}
#endif
