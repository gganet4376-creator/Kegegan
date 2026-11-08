// Capri: a 2D flipbook puppet living in the 3D arena. One material for her and every afterimage;
// frame, flip, glitch, dissolve and tint come from a MaterialPropertyBlock per quad.
// The quad is a view-aligned billboard built in the vertex shader (pivot = cell centre).
// Low preset: one texture fetch. Medium+: chromatic red/green split while glitching.
Shader "Kagegan/Capri"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Grid ("Cells per side", Float) = 8
        _Frame ("Frame", Float) = 0
        _Flip ("Flip X (1 or -1)", Float) = 1
        _Glitch ("Glitch 0..1", Range(0,1)) = 0
        _Dissolve ("Dissolve 0 = whole, 1 = gone", Range(0,1)) = 0
        _Tint ("Afterimage tint", Color) = (1,0.15,0.3,1)
        _TintAmt ("Tint amount", Range(0,1)) = 0
        _Alpha ("Alpha", Range(0,1)) = 1
        _Quality ("Quality 0 low, 1 full", Float) = 1
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Grid, _Frame, _Flip, _Glitch, _Dissolve, _TintAmt, _Alpha, _Quality, _Seed;
            fixed4 _Tint;
            half _KageInk, _KageGlow;
            fixed4 _KageAmbient, _KageLightColor;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 cell : TEXCOORD1; half3 lit : TEXCOORD2; };

            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            v2f vert (appdata v)
            {
                v2f o;
                float3 scale = float3(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21), 1);
                float3 pivot = UnityObjectToViewPos(float3(0, 0, 0));
                float3 vpos = pivot + float3(v.vertex.x * scale.x * _Flip, v.vertex.y * scale.y, 0);
                o.pos = mul(UNITY_MATRIX_P, float4(vpos, 1));
                o.uv = v.uv;
                float f = floor(_Frame + 0.5);
                float col = fmod(f, _Grid);
                float row = _Grid - 1 - floor(f / _Grid); // atlas is filled from the top-left
                o.cell = float4(col, row, 1.0 / _Grid, 0);
                // she is drawn flat-lit; pick up a little of the arena's colour so she doesn't float above it
                o.lit = saturate(lerp(half3(1, 1, 1), _KageAmbient.rgb + _KageLightColor.rgb * 0.75, 0.35));
                return o;
            }

            half4 sampleCell(float2 uv, float4 cell)
            {
                uv = clamp(uv, 0.004, 0.996);
                return tex2D(_MainTex, (cell.xy + uv) * cell.z);
            }

            half4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y;
                // horizontal slice tearing: bands jump sideways, re-rolled ~14 times a second
                float band = floor(uv.y * 18 + _Seed * 7);
                float tick = floor(t * 14 + _Seed * 3);
                float r = hash(float2(band, tick));
                float tear = (r > 1 - _Glitch * 0.55) ? (hash(float2(band, tick + 1)) - 0.5) * 0.22 * _Glitch : 0;
                uv.x += tear;

                half4 c = sampleCell(uv, i.cell);
                if (_Quality > 0.5 && _Glitch > 0.02)
                {
                    float sp = 0.035 * _Glitch;
                    half4 cr = sampleCell(uv + float2(sp, 0), i.cell);
                    half4 cg = sampleCell(uv - float2(sp, 0), i.cell);
                    // red ghost leads, green ghost trails: her two signal colours
                    half a = max(c.a, max(cr.a, cg.a));
                    c.rgb = c.rgb * c.a + half3(1, 0.12, 0.3) * cr.a * (1 - c.a) * 0.9 + half3(0.2, 1, 0.4) * cg.a * (1 - c.a) * 0.9;
                    c.rgb /= max(a, 0.001);
                    c.a = a;
                }
                c.rgb *= i.lit;

                // scanlines + a travelling interference bar
                half scan = 0.9 + 0.1 * sin(uv.y * 260 + t * 30);
                half bar = smoothstep(0.06, 0, abs(frac(uv.y - t * 0.6 + _Seed) - 0.5)) * 0.5 * _Glitch;
                c.rgb = c.rgb * lerp(1, scan, 0.5 + _Glitch * 0.5) + half3(0.3, 1, 0.5) * bar;

                // afterimages / projections
                c.rgb = lerp(c.rgb, _Tint.rgb * (0.6 + 0.4 * dot(c.rgb, 0.33)), _TintAmt);

                // dissolve: rising _Dissolve eats her from the feet up, falling _Dissolve builds her head first, pixel blocks with a red/green burning edge
                if (_Dissolve > 0.001)
                {
                    float2 blk = floor(i.uv * 40);
                    float n = hash(blk + _Seed) * 0.35 + i.uv.y * 0.65; // top keeps the highest n: on appearance the face and eyes show first
                    float d = _Dissolve * 1.12 - 0.06;
                    half edge = smoothstep(d + 0.07, d, n);
                    c.a *= step(d, n);
                    half3 ec = lerp(half3(1, 0.15, 0.35), half3(0.25, 1, 0.45), step(0.5, hash(blk + 3.1)));
                    c.rgb = lerp(c.rgb, ec * 1.6, edge * c.a);
                }

                c.a *= _Alpha;
                c.rgb *= c.a;
                c.rgb *= lerp(1.0, min(max(_KageGlow, 1.0), 2.0), _TintAmt);
                if (_KageInk > 0) c.rgb = lerp(c.rgb, half3(0, 0, 0), _KageInk); // finisher frame: black silhouette
                return min(max(c, 0.0), 4.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
