// Mobile toon shader for Kagegan.
// Lighting comes from global uniforms set by WorldLighting.cs, so the same shader runs unchanged
// under URP (as an SRPDefaultUnlit pass) and the built-in pipeline.
// Vertex color = albedo, vertex alpha = emission amount (eyes, chakra glow, lanterns).
Shader "Kagegan/Toon"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _ShadowTint ("Shadow Tint", Color) = (0.62,0.56,0.72,1)
        _RimColor ("Rim", Color) = (1,0.97,0.9,0.35)
        _Flash ("Hit Flash", Range(0,1)) = 0
        _CharFx ("Character polish (spec + back rim)", Range(0,1)) = 0
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _Emit ("Extra Emission", Range(0,2)) = 0
        _EmitColor ("Extra Emission Color", Color) = (1,0.6,0.2,1)
        _Fade ("Camera Fade (dither)", Range(0,1)) = 1
        _Wind ("Wind sway (grass)", Range(0,1)) = 0
        _GrassContact ("Trample imported grass", Range(0,1)) = 0
        _WindObj ("Wind by object height (trees)", Range(0,1)) = 0
        _WindHeight ("Wind: plant height for full bend (m)", Float) = 8
        _Flutter ("Wind: leaf flutter", Range(0,1)) = 0
        _MainTex ("Albedo (imported models)", 2D) = "white" {}
        _VColor ("Use vertex colour (procedural meshes)", Range(0,1)) = 1
        _Cutoff ("Alpha cutoff (foliage)", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        CGINCLUDE
            #include "UnityCG.cginc"
            fixed4 _Color, _ShadowTint, _RimColor, _FlashColor, _EmitColor;
            half _Flash, _CharFx, _Emit, _Fade, _Wind, _WindObj, _GrassContact, _VColor, _Cutoff, _WindHeight, _Flutter;
            float4 _KageWind;         // xy = wind direction on the ground, z = strength, w = speed
            half _KageGlow;           // emission multiplier (HDR presets)
            sampler2D _MainTex;
            float4 _MainTex_ST;

            float4 _KageLightDir;     // xyz = direction TO the sun
            fixed4 _KageLightColor;
            fixed4 _KageAmbient;
            fixed4 _KageFogColor;
            float4 _KageFogParams;    // x = start, y = end, z = max fog
            float _KageDynLightCount;
            float4 _KageDynPos[4];    // xyz = position, w = range
            float4 _KageDynCol[4];
            half _KageWet;            // weather: rain darkens and glosses the scenery (0..1)
            half _KageSnow;           // weather: snow settles on upward faces of the scenery (0..1)
            #include "KageCasterShadow.cginc"
            // grass trampling: feet, landings, falls and shockwaves (pushed every frame by GrassBend.cs)
            float4 _KageBend[12];     // xyz = contact point, w = radius
            float _KageBendK[12];     // push strength 0..1 (fades out smoothly so the grass springs back)
            float _KageBendCount;
            float4 _KageGrassFade;    // x = distance where grass starts sinking into the ground, y = where it is gone

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Wind for grass, bushes and trees (also used by the depth pass so depth matches the swaying mesh)
            float3 KageWind(float3 wpos, float2 uv, half3 wn, out half gustOut)
            {
                float3 origin = unity_ObjectToWorld._m03_m13_m23;
                float2 dir = _KageWind.xy;
                float str = _KageWind.z * _Wind;
                float t = _Time.y * _KageWind.w;
                // grass: bend by blade uv (root 0, tip 1); trees/bushes: by height above the model's base
                float objB = saturate((wpos.y - origin.y) / _WindHeight);
                float bend = lerp(uv.y * uv.y, objB * objB, _WindObj);
                float amp = lerp(0.38, 0.05 * _WindHeight + 0.16, _WindObj);
                float tall = lerp(0.45, _WindHeight, _WindObj);
                // big gusts roll across the field as visible waves
                float along = dot(wpos.xz, dir);
                float g1 = sin(along * 0.16 - t * 1.4) * 0.5 + 0.5;
                float g2 = sin(along * 0.05 + wpos.x * 0.02 - t * 0.55) * 0.5 + 0.5;
                float gust = g1 * g1 * (0.4 + 0.6 * g2);
                // every clump / tree also rocks on its own rhythm
                float2 anchor = lerp(wpos.xz, origin.xz + wpos.xz * 0.1, _WindObj);
                float ph = dot(anchor, float2(0.73, 1.21));
                float sway = sin(t * 2.1 + ph) * 0.7 + sin(t * 3.7 + ph * 1.7) * 0.45;
                float2 side = float2(-dir.y, dir.x);
                float2 off = (dir * ((0.25 + gust) + sway * 0.35) + side * sway * 0.2) * amp * bend * str;
                // trampling: blades lean away from every nearby contact point (grass only, not trees)
                float grass = 1.0 - _WindObj;
                float press = 0.0;
                if (max(grass, _GrassContact) > 0.5 && _KageBendCount > 0.5)
                {
                    float2 push = 0;
                    for (int bi = 0; bi < 12; bi++)
                    {
                        if (bi >= (int)_KageBendCount) break;
                        float4 b = _KageBend[bi];
                        float2 dv = wpos.xz - b.xz;
                        float dist = length(dv);
                        float k = saturate(1.0 - dist / max(b.w, 0.01));
                        k = k * k * (3.0 - 2.0 * k) * _KageBendK[bi] * saturate(1.6 - abs(wpos.y - b.y));
                        push += dv / max(dist, 0.04) * k;
                        press = max(press, k);
                    }
                    float pl = length(push);
                    push *= min(pl, 1.0) / max(pl, 0.001);
                    off = off * (1.0 - min(pl, 1.0) * 0.6) + push * 0.42 * bend;
                }
                float3 d = float3(off.x, 0, off.y);
                d.y = -dot(off, off) / (2.0 * tall) - press * lerp(uv.y, objB, _WindObj) * 0.18;
                // distant grass sinks into the ground (saves fill rate, no popping at the draw distance)
                if (grass > 0.5 && _KageGrassFade.y > 0)
                {
                    float cd = distance(wpos.xz, _WorldSpaceCameraPos.xz);
                    float fade = saturate((cd - _KageGrassFade.x) / max(_KageGrassFade.y - _KageGrassFade.x, 0.1));
                    d.y -= fade * uv.y * 0.9;
                }
                // leaves shiver
                float f = sin(t * 11.0 + dot(wpos, float3(3.7, 5.3, 4.1))) * (0.3 + gust) * _Flutter * str;
                d += wn * f * 0.035 * saturate(bend * 4.0);
                gustOut = gust * str;
                return wpos + d;
            }
        ENDCG
        Pass
        {
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float3 wpos : TEXCOORD0;
                half3 wnormal : TEXCOORD1;
                float4 spos : TEXCOORD2;
                float2 uv : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                half3 wn = UnityObjectToWorldNormal(v.normal);
                if (_Wind > 0)
                {
                    half gust;
                    o.wpos = KageWind(o.wpos, v.uv, wn, gust);
                    // the passing gust catches the light on grass tips
                    v.color.rgb *= 1.0 + gust * 0.14 * v.uv.y * (1.0 - _WindObj);
                }
                o.pos = UnityWorldToClipPos(o.wpos);
                o.wnormal = wn;
                o.color = lerp(fixed4(1, 1, 1, 0), v.color, _VColor);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.spos = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // screen-door fade for props that block the camera's view of the fighters
                if (_Fade < 0.999)
                {
                    float2 px = floor(i.spos.xy / max(i.spos.w, 1e-4) * _ScreenParams.xy);
                    float d = frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715)))); // interleaved gradient noise
                    clip(_Fade - d);
                }
                fixed4 tex = tex2D(_MainTex, i.uv);
                if (_Cutoff > 0) clip(tex.a - _Cutoff);
                half3 n = normalize(i.wnormal);
                half3 l = normalize(_KageLightDir.xyz);
                half3 vdir = normalize(_WorldSpaceCameraPos.xyz - i.wpos);
                half ndl = dot(n, l);
                // two-band anime ramp
                half band1 = smoothstep(-0.02, 0.05, ndl);
                half band2 = smoothstep(0.55, 0.62, ndl) * 0.12;
                half3 albedo = i.color.rgb * tex.rgb * _Color.rgb;
                // weather on scenery only (fighters have _CharFx > 0; glowing parts stay as they are)
                half scenery = (1.0 - saturate(_CharFx * 10.0)) * (1.0 - i.color.a);
                half wetHere = 0;
                if (_KageSnow > 0)
                {
                    half cover = smoothstep(0.5, 0.85, n.y + frac(sin(dot(floor(i.wpos.xz * 3.0), float2(12.99, 78.23))) * 437.58) * 0.12) * _KageSnow * scenery;
                    albedo = lerp(albedo, half3(0.88, 0.9, 0.96), cover);
                }
                if (_KageWet > 0)
                {
                    wetHere = _KageWet * scenery * saturate(n.y * 0.6 + 0.6);
                    albedo *= 1.0 - 0.28 * wetHere;
                }
                // fighters' sun shadows on the scenery (High / Ultra), and a sky-vs-ground ambient instead of a flat one
                half shAll = 1.0;
                if (_KageCasterCount > 0 && scenery > 0) { half sh = KageCasterShadow(i.wpos, l); band1 *= sh; band2 *= sh; shAll = sh; }
                // trees / buildings shade the ground, the grass and the fighters walking under them
                { half ss = KageSceneShadow(i.wpos, l); band1 *= ss; band2 *= ss; shAll *= ss; }
                half hemi = lerp(0.82, 1.1, saturate(n.y * 0.5 + 0.5));
                // shadows also block part of the sky light, so they stay readable under overcast / night lighting
                half ambOcc = lerp(1.0, shAll, 0.45);
                half3 lit = albedo * lerp(_ShadowTint.rgb, half3(1,1,1), band1) * (_KageLightColor.rgb + band2) + albedo * _KageAmbient.rgb * hemi * ambOcc;

                half fres = 1.0 - saturate(dot(n, vdir));
                half rim = pow(fres, 4.0) * _RimColor.a * (0.35 + band1);
                rim *= 1.0 - 0.85 * scenery * saturate(n.y); // no milky sheen on ground seen at a grazing angle
                lit += rim * _RimColor.rgb;
                if (wetHere > 0)
                {
                    // wet sheen: a tight sun glint plus the grey sky on grazing angles
                    half3 hw = normalize(l + vdir);
                    lit += _KageLightColor.rgb * pow(saturate(dot(n, hw)), 70.0) * 0.6 * wetHere * band1;
                    lit += _KageFogColor.rgb * pow(fres, 3.0) * 0.18 * wetHere;
                }
                if (_CharFx > 0)
                {
                    // anime back-light: a crisp sun-coloured edge on the side away from the camera's light
                    half back = saturate(dot(l, -vdir) * 0.6 + 0.4);
                    half edge = smoothstep(0.62, 0.72, fres) * back * saturate(ndl * 0.8 + 0.5);
                    lit += edge * _KageLightColor.rgb * albedo * 0.9 * _CharFx;
                    // hard-edged toon highlight (hair, cloth folds, skin)
                    half3 hv = normalize(l + vdir);
                    half spec = smoothstep(0.93, 0.96, dot(n, hv)) * band1;
                    lit += spec * _KageLightColor.rgb * 0.22 * _CharFx;
                }

                // cheap point lights from explosions, fire and chakra
                for (int k = 0; k < 4; k++)
                {
                    if (k < (int)_KageDynLightCount)
                    {
                        float3 d = _KageDynPos[k].xyz - i.wpos;
                        float dist = length(d);
                        half att = saturate(1.0 - dist / max(0.01, _KageDynPos[k].w));
                        att *= att;
                        half ndp = saturate(dot(n, d / max(dist, 0.001)) * 0.6 + 0.4);
                        lit += albedo * _KageDynCol[k].rgb * att * ndp;
                    }
                }

                half glow = max(_KageGlow, 1.0); // > 1 when HDR + bloom are on, so glowing parts bloom
                half3 col = lerp(lit, albedo * 1.35 * glow, i.color.a);
                col += _EmitColor.rgb * _Emit * glow;
                col = lerp(col, _FlashColor.rgb, _Flash);

                float fogDist = length(i.wpos - _WorldSpaceCameraPos.xyz);
                half fog = saturate((fogDist - _KageFogParams.x) / max(0.01, _KageFogParams.y - _KageFogParams.x)) * _KageFogParams.z;
                col = lerp(col, _KageFogColor.rgb, fog);
                return fixed4(col, 1);
            }
            ENDCG
        }
        // depth for URP's depth texture (background blur on High/Ultra)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            #pragma multi_compile_instancing
            #pragma target 3.0
            struct v2fD { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2fD vertD (appdata v)
            {
                v2fD o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                if (_Wind > 0)
                {
                    half gust;
                    wpos = KageWind(wpos, v.uv, UnityObjectToWorldNormal(v.normal), gust);
                }
                o.pos = UnityWorldToClipPos(wpos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            fixed4 fragD (v2fD i) : SV_Target
            {
                if (_Cutoff > 0) clip(tex2D(_MainTex, i.uv).a - _Cutoff);
                if (_Fade < 0.999) clip(-1);
                return 0;
            }
            ENDCG
        }
    }
    Fallback Off
}
