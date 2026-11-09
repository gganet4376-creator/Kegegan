// Shared atmosphere for the Toon and Surface shaders. Include after _KageFogColor and _KageLightColor are declared.
#ifndef KAGE_ATMO
#define KAGE_ATMO
float4 _KageHeightFog;    // x = ground height, y = thinning per metre up, z = density (0 = off), w = distance where it starts
half _KageFogSun;         // 0..1: the haze turns sun-coloured when looking into the light (0 = off)

// Height fog (mist lying low in the distance) and sun in-scattering (the haze glows warm toward the sun).
// Adds to the distance fog in `fog`; returns the fog colour to blend to.
half3 KageFogTint(float dist, float y, half3 viewDir, half3 l, inout half fog)
{
    if (_KageHeightFog.z > 0)
    {
        half hf = exp2(-max(y - _KageHeightFog.x, 0.0) * _KageHeightFog.y)
                * saturate((dist - _KageHeightFog.w) / 30.0) * _KageHeightFog.z;
        fog = 1.0 - (1.0 - fog) * (1.0 - hf);
    }
    half3 c = _KageFogColor.rgb;
    if (_KageFogSun > 0)
    {
        half sv = saturate(dot(viewDir, l));
        sv *= sv; sv *= sv; sv *= sv;
        c = lerp(c, _KageLightColor.rgb, sv * _KageFogSun);
    }
    return c;
}
#endif
