// Inverted-hull outline used as a second material on characters (disabled on the Low preset).
Shader "Kagegan/Outline"
{
    Properties
    {
        _OutlineColor ("Color", Color) = (0.08,0.05,0.1,1)
        _Width ("Width (px at 1080p)", Float) = 2.6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Cull Front
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _OutlineColor;
            float _Width;
            float _KageOutlineScale;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 nView = mul((float3x3)UNITY_MATRIX_V, UnityObjectToWorldNormal(v.normal));
                float2 off = mul((float2x2)UNITY_MATRIX_P, nView.xy);
                float len = max(length(off), 1e-4);
                float scale = max(_KageOutlineScale, 0.3);
                o.pos.xy += (off / len) * (_Width * scale * 2.0 / max(_ScreenParams.y, 1.0)) * o.pos.w;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return _OutlineColor; }
            ENDCG
        }
    }
    Fallback Off
}
