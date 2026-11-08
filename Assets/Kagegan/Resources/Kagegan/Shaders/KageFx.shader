// Unlit vertex-colored effect shader (particles, slashes, shockwaves, beams, sky dome, ghosts).
// Blend mode is chosen per material: additive (One One) or premultiplied alpha (One OneMinusSrcAlpha).
Shader "Kagegan/Fx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        _Fog ("Fog amount", Range(0,1)) = 0
        _Lit ("Scene lit (smoke, dust)", Range(0,1)) = 0
        _Soft ("Soft edge vs scene", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ KAGE_SOFTFX
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Fog, _Lit, _Soft;
            fixed4 _KageAmbient, _KageLightColor;
            #ifdef KAGE_SOFTFX
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            #endif
            half _SrcBlend, _DstBlend;
            half _KageGlow;   // > 1 on HDR presets so additive effects bloom
            fixed4 _KageFogColor;
            float4 _KageFogParams;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; half fog : TEXCOORD1;
                #ifdef KAGE_SOFTFX
                float4 proj : TEXCOORD2;
                #endif
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(wpos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                // smoke and dust take the scene's light, so they don't glow white in a night or storm arena
                half3 sceneLit = min(_KageAmbient.rgb + _KageLightColor.rgb * 0.6, 1.2);
                o.color.rgb *= lerp(half3(1, 1, 1), sceneLit, _Lit);
                #ifdef KAGE_SOFTFX
                o.proj = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.proj.z);
                #endif
                float d = length(wpos - _WorldSpaceCameraPos.xyz);
                o.fog = saturate((d - _KageFogParams.x) / max(0.01, _KageFogParams.y - _KageFogParams.x)) * _KageFogParams.z * _Fog;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 c = tex2D(_MainTex, i.uv) * i.color;
                #ifdef KAGE_SOFTFX
                // soft particles: fade where the sprite cuts into the ground / walls instead of a hard line
                float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.proj)));
                c.a *= lerp(1.0, saturate((sceneZ - i.proj.z) * 2.5), _Soft);
                #endif
                c.rgb *= c.a; // premultiplied: works for additive (One One) and alpha (One OneMinusSrcAlpha)
                c.rgb = lerp(c.rgb, _KageFogColor.rgb * c.a, i.fog);
                if (_DstBlend == 1) c.rgb *= max(_KageGlow, 1.0); // additive glow
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
