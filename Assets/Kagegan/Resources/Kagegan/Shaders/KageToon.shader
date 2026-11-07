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
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _Emit ("Extra Emission", Range(0,2)) = 0
        _EmitColor ("Extra Emission Color", Color) = (1,0.6,0.2,1)
        _Fade ("Camera Fade (dither)", Range(0,1)) = 1
        _Wind ("Wind sway (grass)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color, _ShadowTint, _RimColor, _FlashColor, _EmitColor;
            half _Flash, _Emit, _Fade, _Wind;

            float4 _KageLightDir;     // xyz = direction TO the sun
            fixed4 _KageLightColor;
            fixed4 _KageAmbient;
            fixed4 _KageFogColor;
            float4 _KageFogParams;    // x = start, y = end, z = max fog
            float _KageDynLightCount;
            float4 _KageDynPos[4];    // xyz = position, w = range
            float4 _KageDynCol[4];

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float3 wpos : TEXCOORD0;
                half3 wnormal : TEXCOORD1;
                float4 spos : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                if (_Wind > 0)
                {
                    // grass blades: tips (uv.y = 1) bend with a travelling gust
                    float gust = sin(_Time.y * 1.7 + o.wpos.x * 0.35 + o.wpos.z * 0.22) * 0.6 + sin(_Time.y * 3.1 + o.wpos.z * 0.9) * 0.25;
                    o.wpos.xz += float2(0.08, 0.05) * gust * v.uv.y * _Wind;
                }
                o.pos = UnityWorldToClipPos(o.wpos);
                o.wnormal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
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
                half3 n = normalize(i.wnormal);
                half3 l = normalize(_KageLightDir.xyz);
                half3 vdir = normalize(_WorldSpaceCameraPos.xyz - i.wpos);
                half ndl = dot(n, l);
                // two-band anime ramp
                half band1 = smoothstep(-0.02, 0.05, ndl);
                half band2 = smoothstep(0.55, 0.62, ndl) * 0.12;
                half3 albedo = i.color.rgb * _Color.rgb;
                half3 lit = albedo * lerp(_ShadowTint.rgb, half3(1,1,1), band1) * (_KageLightColor.rgb + band2) + albedo * _KageAmbient.rgb;

                half rim = pow(1.0 - saturate(dot(n, vdir)), 4.0) * _RimColor.a * (0.35 + band1);
                lit += rim * _RimColor.rgb;

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

                half3 col = lerp(lit, albedo * 1.35, i.color.a);
                col += _EmitColor.rgb * _Emit;
                col = lerp(col, _FlashColor.rgb, _Flash);

                float fogDist = length(i.wpos - _WorldSpaceCameraPos.xyz);
                half fog = saturate((fogDist - _KageFogParams.x) / max(0.01, _KageFogParams.y - _KageFogParams.x)) * _KageFogParams.z;
                col = lerp(col, _KageFogColor.rgb, fog);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
