// Photo-textured surfaces (full on High/Ultra; KAGE_SURF_LITE = albedo-only ground on Low/Medium): arena ground, moon temple tiles and the
// photoscanned Poly Haven props. Same global lighting, fog and dynamic lights as Kagegan/Toon, but with
// albedo + normal maps, a softer light ramp, anti-tiling and (City) wet asphalt with puddles that
// mirror the sky and rain ripples.
// Vertex colour * 2 tints the texture (0.5 = neutral), vertex alpha = emission (like Toon).
Shader "Kagegan/Surface"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal (OpenGL)", 2D) = "bump" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ShadowTint ("Shadow Tint", Color) = (0.62,0.56,0.72,1)
        _BumpScale ("Normal strength", Range(0,2)) = 1
        _ObjUV ("Tile size in metres (0 = use mesh UVs)", Float) = 0
        _AntiTile ("Anti-tiling", Range(0,1)) = 0
        _Wet ("Wet (puddles + sky reflection)", Range(0,1)) = 0
        _Gloss ("Specular", Range(0,1)) = 0.08
        _Fade ("Camera Fade (dither)", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        CGINCLUDE
            #include "UnityCG.cginc"
            fixed4 _Color, _ShadowTint;
            half _BumpScale, _AntiTile, _Wet, _Gloss, _Fade;
            float _ObjUV;
            sampler2D _MainTex, _BumpMap;
            float4 _MainTex_ST;
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
        ENDCG
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local __ KAGE_SURF_LITE
            #pragma target 3.0

            float4 _KageLightDir;
            fixed4 _KageLightColor;
            fixed4 _KageAmbient;
            fixed4 _KageFogColor;
            float4 _KageFogParams;
            float _KageDynLightCount;
            float4 _KageDynPos[4];
            float4 _KageDynCol[4];
            half _KageGlow;
            half _KageInk, _KageEnvMute;
            sampler2D _KageSkyTex;     // the arena's sky panorama (top 55% of an equirect image)
            fixed4 _KageSkyTint;
            half _KageWet;             // weather: rain soaks every photo surface (0..1)
            half _KageSnow;            // weather: snow settles on upward faces (0..1)
            #include "KageCasterShadow.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float3 wpos : TEXCOORD0;
                float3 opos : TEXCOORD1;
                half3 onormal : TEXCOORD2;
                half3 wnormal : TEXCOORD3;
                half4 wtangent : TEXCOORD4;
                float2 uv : TEXCOORD5;
                float4 spos : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.opos = v.vertex.xyz;
                o.onormal = v.normal;
                o.wnormal = UnityObjectToWorldNormal(v.normal);
                o.wtangent = half4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w * unity_WorldTransformParams.w);
                o.pos = UnityWorldToClipPos(o.wpos);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.spos = ComputeScreenPos(o.pos);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1.0), f.x), f.y);
            }

            // albedo + tangent-space normal, with a second rotated, larger sample blended in by noise so the repeat disappears
            void SampleSurf(float2 uv, out half4 alb, out half3 nt)
            {
                alb = tex2D(_MainTex, uv);
            #if defined(KAGE_SURF_LITE)
                nt = half3(0, 0, 1); // Low/Medium: one texture fetch, flat normal
            #else
                nt = UnpackNormal(tex2D(_BumpMap, uv));
                if (_AntiTile > 0)
                {
                    float2 uv2 = float2(uv.x * 0.8 - uv.y * 0.6, uv.x * 0.6 + uv.y * 0.8) * 0.43 + 0.37;
                    half m = smoothstep(0.3, 0.7, Noise(uv * 0.23)) * _AntiTile;
                    alb = lerp(alb, tex2D(_MainTex, uv2), m);
                    nt = lerp(nt, UnpackNormal(tex2D(_BumpMap, uv2)), m);
                }
                nt.xy *= _BumpScale;
            #endif
            }

            half3 SkyLookup(half3 r)
            {
                float u = atan2(r.z, r.x) * 0.15915494;
                float v = 1.0 - acos(clamp(r.y, -1.0, 1.0)) * 0.31830989 / 0.55;
                return tex2Dlod(_KageSkyTex, float4(u, saturate(v), 0, 1.5)).rgb * _KageSkyTint.rgb;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (_Fade < 0.999)
                {
                    float2 px = floor(i.spos.xy / max(i.spos.w, 1e-4) * _ScreenParams.xy);
                    float d = frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715))));
                    clip(_Fade - d);
                }
                half3 gn = normalize(i.wnormal);
                half4 alb; half3 nt; half3 n;
                if (_ObjUV > 0)
                {
                    // box projection in object space (ground disc, temple tiles): pick the face's dominant axis
                    half3 an = abs(i.onormal);
                    float2 uv; half3 t, b;
                    if (an.y >= an.x && an.y >= an.z) { uv = i.opos.xz; t = half3(1, 0, 0); b = half3(0, 0, 1); }
                    else if (an.x >= an.z) { uv = i.opos.zy; t = half3(0, 0, 1); b = half3(0, 1, 0); }
                    else { uv = i.opos.xy; t = half3(1, 0, 0); b = half3(0, 1, 0); }
                    SampleSurf(uv / _ObjUV, alb, nt);
                    t = normalize(mul((float3x3)unity_ObjectToWorld, t));
                    b = normalize(mul((float3x3)unity_ObjectToWorld, b));
                    n = normalize(t * nt.x + b * nt.y + gn * nt.z);
                }
                else
                {
                    SampleSurf(i.uv, alb, nt);
                    half3 t = normalize(i.wtangent.xyz);
                    half3 b = cross(gn, t) * i.wtangent.w;
                    n = normalize(t * nt.x + b * nt.y + gn * nt.z);
                }

                half3 vdir = normalize(_WorldSpaceCameraPos.xyz - i.wpos);
                half3 l = normalize(_KageLightDir.xyz);
                half3 albedo = alb.rgb * _Color.rgb * i.color.rgb * 2.0;

                // wet asphalt: a damp sheen everywhere, puddles where the noise dips, rain rings inside them
                half puddle = 0, wet = 0;
                half wetAmt = max(_Wet, _KageWet * saturate(gn.y * 1.5 + 0.35)); // walls dry faster than floors
            #if defined(KAGE_SURF_LITE)
                if (wetAmt > 0) { wet = 0.4 * wetAmt; albedo *= lerp(1.0, 0.6, wet); } // just darken: no puddles or ripples
            #else
                if (wetAmt > 0)
                {
                    float2 p = i.wpos.xz;
                    float pn = Noise(p * 0.17) * 0.65 + Noise(p * 0.61 + 7.3) * 0.35;
                    puddle = smoothstep(0.5, 0.58, pn) * wetAmt * saturate(gn.y * 2.0 - 1.0);
                    wet = max(puddle, 0.4 * wetAmt);
                    albedo *= lerp(1.0, 0.5, wet);
                    // rain ripples: one ring per cell, each on its own clock
                    float2 cell = floor(p * 1.4);
                    float2 f = frac(p * 1.4) - 0.5;
                    float h = Hash(cell);
                    float2 c = float2(Hash(cell + 3.1), Hash(cell + 5.7)) * 0.5 - 0.25;
                    float t = frac(_Time.y * 0.9 + h);
                    float2 dv = f - c;
                    float d = length(dv);
                    float ring = sin((d - t * 0.45) * 55.0) * smoothstep(0.45, 0.0, d) * (1.0 - t) * smoothstep(t * 0.45 + 0.08, t * 0.45, d);
                    half3 calm = normalize(lerp(n, gn, 0.85));
                    n = normalize(lerp(n, calm, puddle) + half3(dv.x, 0, dv.y) / max(d, 0.01) * ring * 0.12 * puddle);
                }
            #endif

                // settled snow: fills the up-facing parts first (normal map gives it drifts), hides puddles
                if (_KageSnow > 0)
                {
                    half cover = smoothstep(0.45, 0.9, n.y + (Noise(i.wpos.xz * 0.9) - 0.5) * 0.35) * _KageSnow * (1.0 - i.color.a);
                    albedo = lerp(albedo, half3(0.86, 0.89, 0.95), cover);
                    wet *= 1.0 - cover; puddle *= 1.0 - cover;
                    n = normalize(lerp(n, gn, cover * 0.6));
                }

                half ndl = dot(n, l);
                half band = smoothstep(-0.12, 0.35, ndl);
                half shAll = KageSceneShadow(i.wpos, l);
                if (_KageCasterCount > 0) shAll *= KageCasterShadow(i.wpos, l);
                band *= shAll;
                // shadows also block part of the sky light, so they stay readable under overcast / night lighting
                half3 lit = albedo * lerp(_ShadowTint.rgb, half3(1, 1, 1), band) * _KageLightColor.rgb + albedo * _KageAmbient.rgb * lerp(1.0, shAll, 0.45);
                half3 hv = normalize(l + vdir);
                half gloss = lerp(_Gloss, 1.0, wet);
                lit += _KageLightColor.rgb * pow(saturate(dot(n, hv)), lerp(24.0, 160.0, wet)) * gloss * band * lerp(0.25, 2.0, wet);

                for (int k = 0; k < 4; k++)
                {
                    if (k < (int)_KageDynLightCount)
                    {
                        float3 d = _KageDynPos[k].xyz - i.wpos;
                        float dist = length(d);
                        half att = saturate(1.0 - dist / max(0.01, _KageDynPos[k].w));
                        att *= att;
                        half3 ld = d / max(dist, 0.001);
                        half ndp = saturate(dot(n, ld) * 0.6 + 0.4);
                        lit += albedo * _KageDynCol[k].rgb * att * ndp;
                        // fire and chakra glint in the wet street
                        lit += _KageDynCol[k].rgb * att * pow(saturate(dot(n, normalize(ld + vdir))), 60.0) * wet * 1.5;
                    }
                }

            #if !defined(KAGE_SURF_LITE)
                if (wetAmt > 0)
                {
                    half fres = 0.04 + 0.96 * pow(1.0 - saturate(dot(n, vdir)), 5.0);
                    half3 refl = SkyLookup(reflect(-vdir, n));
                    lit = lerp(lit, refl, saturate(fres * lerp(0.35, 1.0, puddle) * wet + puddle * 0.25));
                }
            #endif

                half glow = max(_KageGlow, 1.0);
                half3 col = lerp(lit, i.color.rgb * 1.35 * glow, i.color.a); // glowing inlays, same brightness as Toon

                float fogDist = length(i.wpos - _WorldSpaceCameraPos.xyz);
                half fog = saturate((fogDist - _KageFogParams.x) / max(0.01, _KageFogParams.y - _KageFogParams.x)) * _KageFogParams.z;
                if (_KageEnvMute > 0)
                {
                    half lum = dot(col, half3(0.299, 0.587, 0.114));
                    col = lerp(col, lerp(col, lum.xxx * half3(1.02, 1.0, 0.96), 0.55), _KageEnvMute * (1.0 - i.color.a));
                }
                col = lerp(col, _KageFogColor.rgb, fog);
                if (_KageInk > 0) col = lerp(col, half3(0.97, 0.95, 0.9), _KageInk);
                return fixed4(col, 1);
            }
            ENDCG
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            CGPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            #pragma multi_compile_instancing
            #pragma target 3.0
            struct v2fD { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2fD vertD (appdata v)
            {
                v2fD o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }
            fixed4 fragD (v2fD i) : SV_Target
            {
                if (_Fade < 0.999) clip(-1);
                return 0;
            }
            ENDCG
        }
    }
    Fallback Off
}
