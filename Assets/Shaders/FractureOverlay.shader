Shader "Custom/FractureOverlay"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0, 0, 0.6)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent+500"
            "RenderType"     = "Transparent"
        }

        Pass
        {
            Name "FractureOverlay"
            Tags { "LightMode" = "UniversalForward" }

            ZTest  Always               // sempre visibile, ignora il depth buffer
            ZWrite Off
            Cull   Off                  // entrambe le facce
            Blend  SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(_Color.rgb, _Color.a);
            }
            ENDHLSL
        }
    }
}
