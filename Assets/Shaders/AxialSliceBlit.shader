// AxialSliceBlit.shader
// Blit shader per la visualizzazione 2D della slice assiale CT.
// Campiona la texture 3D del volume a una posizione fissa sull'asse cranio-caudale
// e applica la stessa Transfer Function usata dal volume renderer.
//
// Coordinate 3D texture:
//   u (x) = asse i NIfTI → Unity X (sinistra-destra paziente)
//   v (y) = asse j NIfTI → Unity Z (anteriore-posteriore)
//   w (z) = asse k NIfTI → Unity Y (cranio-caudale) ← questo è _SliceY
//
// Con _TiltAngle != 0, il piano ruota attorno all'asse U (sinistra-destra):
//   dv = distanza dal centro in V (0.5 - screen_v)
//   v_new = 0.5 + dv * cos(θ)       ← V si comprime
//   w_new = _SliceY + dv * sin(θ)   ← W varia lungo A-P

Shader "Custom/AxialSliceBlit"
{
    Properties
    {
        _DataTex      ("Volume Data (3D)", 3D) = "" {}
        _TFTex        ("Transfer Function", 2D) = "white" {}
        _SliceY       ("Slice k [0,1]", Float) = 0.5
        _TiltAngle    ("Tilt angle (radians)", Float) = 0.0
        _UseGrayscale ("Grayscale mode (0=TF, 1=gray)", Float) = 0.0
        _WinLo        ("Window low [0,1]", Float) = 0.25
        _WinHi        ("Window high [0,1]", Float) = 0.65
    }

    // ── SubShader URP ────────────────────────────────────────────────────────────
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv     : TEXCOORD0;
            };

            Texture3D _DataTex;  SamplerState sampler_DataTex;
            Texture2D _TFTex;    SamplerState sampler_TFTex;
            float _SliceY;
            float _TiltAngle;
            float _UseGrayscale;
            float _WinLo;
            float _WinHi;

            v2f vert(appdata v)
            {
                v2f o;
                // Per Graphics.Blit la MVP è identity: TransformObjectToHClip è passthrough
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv     = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                // Distanza dal centro in V (con flip Y: 0.5 - screen_v)
                float dv = 0.5 - i.uv.y;

                // Piano inclinato attorno all'asse U (sinistra-destra del paziente):
                //   θ=0 → piano assiale piatto (equivalente a formula originale)
                //   θ>0 → lato posteriore (dv>0) sale cranialmente
                float3 uvw = float3(
                    i.uv.x,
                    0.5 + dv * cos(_TiltAngle),
                    _SliceY + dv * sin(_TiltAngle)
                );

                // Pixel fuori dal volume → nero
                if (any(uvw < 0.0) || any(uvw > 1.0))
                    return half4(0.0, 0.0, 0.0, 1.0);

                float dataVal = _DataTex.Sample(sampler_DataTex, uvw).r;

                half4 col;
                if (_UseGrayscale > 0.5)
                {
                    // Scala di grigi con windowing: _WinLo → nero, _WinHi → bianco
                    half gray = saturate((dataVal - _WinLo) / max(_WinHi - _WinLo, 0.001));
                    col = half4(gray, gray, gray, 1.0);
                }
                else
                {
                    col   = _TFTex.Sample(sampler_TFTex, float2(dataVal, 0.0));
                    col.a = 1.0;
                }
                return col;
            }
            ENDHLSL
        }
    }

    // ── SubShader Built-in fallback ──────────────────────────────────────────────
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler3D _DataTex;
            sampler2D _TFTex;
            float     _SliceY;
            float     _TiltAngle;
            float     _UseGrayscale;
            float     _WinLo;
            float     _WinHi;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float dv = 0.5 - i.uv.y;
                float3 uvw = float3(
                    i.uv.x,
                    0.5 + dv * cos(_TiltAngle),
                    _SliceY + dv * sin(_TiltAngle)
                );
                if (uvw.x < 0 || uvw.x > 1 || uvw.y < 0 || uvw.y > 1 || uvw.z < 0 || uvw.z > 1)
                    return fixed4(0, 0, 0, 1);
                float dataVal = tex3D(_DataTex, uvw).r;
                if (_UseGrayscale > 0.5)
                {
                    fixed gray = saturate((dataVal - _WinLo) / max(_WinHi - _WinLo, 0.001));
                    return fixed4(gray, gray, gray, 1.0);
                }
                fixed4 col = tex2D(_TFTex, float2(dataVal, 0.0));
                col.a = 1.0;
                return col;
            }
            ENDCG
        }
    }
}
