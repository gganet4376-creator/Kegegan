// Soft sun shadows of the fighters on the scenery (High / Ultra).
// Each fighter is a vertical capsule; a surface point is shadowed when the ray from it towards the sun passes
// close to a capsule. The penumbra widens with the distance to the caster, like a real contact-hardening shadow.
// Cost: a few dozen ALU per caster, no shadow map, no extra pass. Count is 0 on Low / Medium.
#ifndef KAGE_CASTER_SHADOW
#define KAGE_CASTER_SHADOW

float4 _KageCasters[6];   // xyz = feet position, w = body height (radius is derived from it)
float _KageCasterCount;
half _KageCasterStrength;

half KageCasterShadow(float3 p, half3 l)
{
    half lit = 1.0;
    for (int k = 0; k < 6; k++)
    {
        if (k < (int)_KageCasterCount)
        {
            float4 c = _KageCasters[k];
            float rad = c.w * 0.17;
            float3 a = c.xyz + float3(0, rad, 0);
            float3 ab = float3(0, max(c.w - 2.0 * rad, 0.01), 0);
            float3 r = p - a;
            // closest points between the ray p + s*l (s >= 0) and the capsule's axis a + t*ab (0..1)
            float b = dot(l, ab);
            float cc = dot(l, r);
            float e = dot(ab, ab);
            float f = dot(ab, r);
            float s = max(0.0, (b * f - cc * e) / max(e - b * b, 1e-4));
            float t = saturate((b * s + f) / e);
            s = max(0.0, b * t - cc);
            float d = length(r + l * s - ab * t);
            half occ = 1.0 - smoothstep(rad * 0.45, rad * 1.25 + s * 0.1, d);
            occ *= saturate(1.0 - s * 0.07);       // very long dawn shadows thin out
            occ *= saturate(s * 4.0);              // nothing at the caster's own feet line (blob covers contact)
            lit = min(lit, 1.0 - occ * _KageCasterStrength);
        }
    }
    return lit;
}

// Baked sun shadows of the static scenery (trees, buildings, gates) on the ground, grass and fighters (High / Ultra).
// SceneryShadow.cs projects every static triangle along the sun onto the ground plane once per arena:
// R = soft coverage, G = height of the highest occluder / 32 m. A point is shadowed when an occluder sits on its
// ray to the sun above it. One texture read, no shadow map pass.
sampler2D _KageSceneShadow;
float4 _KageSceneShadowRect;   // xy = world x/z of the texture's corner, z = 1 / size, w = strength (0 = off)

half KageSceneShadow(float3 p, half3 l)
{
    if (_KageSceneShadowRect.w <= 0) return 1.0;
    float2 q = p.xz - l.xz * (p.y / max(l.y, 0.15));
    float2 uv = (q - _KageSceneShadowRect.xy) * _KageSceneShadowRect.z;
    half4 s = tex2D(_KageSceneShadow, uv);
    half inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    half occ = s.r * saturate((s.g * 32.0 - p.y - 0.3) * 2.0) * inside;
    return 1.0 - occ * _KageSceneShadowRect.w;
}
#include "KageShadowMap.cginc"
#endif
