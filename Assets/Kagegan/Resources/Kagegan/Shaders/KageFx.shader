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
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Fog, _Lit, _Soft;
            fixed4 _KageAmbient, _KageLightColor;
            half _SrcBlend, _DstBlend;
            half _KageGlow;   // > 1 on HDR presets so additive effects bloom
            fixed4 _KageFogColor;
            float4 _KageFogParams;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; half fog : TEXCOORD1;
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
                // smoke, dust and mist (_Soft) thin out right in front of the lens instead of filling the screen.
                // (No depth-texture soft particles: on some Android GPUs that read produced Inf/NaN and the puffs
                // came out as solid white blobs.)
                float eyeZ = -mul(UNITY_MATRIX_V, float4(wpos, 1.0)).z;
                o.color.a *= lerp(1.0, saturate((eyeZ - 1.2) / 4.0), _Soft);
                float d = length(wpos - _WorldSpaceCameraPos.xyz);
                o.fog = saturate((d - _KageFogParams.x) / max(0.01, _KageFogParams.y - _KageFogParams.x)) * _KageFogParams.z * _Fog;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 c = saturate(tex2D(_MainTex, i.uv) * i.color);
                c.rgb *= c.a; // premultiplied: works for additive (One One) and alpha (One OneMinusSrcAlpha)
                c.rgb = lerp(c.rgb, _KageFogColor.rgb * c.a, i.fog);
                if (_DstBlend == 1) c.rgb *= min(max(_KageGlow, 1.0), 3.0); // additive glow
                return min(max(c, 0.0), 4.0); // never hand the HDR target anything that blooms into a white blob

            }
            ENDCG
        }
    }
    Fallback Off
}
