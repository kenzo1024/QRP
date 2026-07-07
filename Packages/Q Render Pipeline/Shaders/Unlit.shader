Shader "QRP/Unlit"
{
    Properties
    {
        [MatDataTransfer][MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MatDataTransfer] _TestFloat("Test RGB Brightness", Range(-1.0, 1.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "Unlit"
            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _TestFloat;
                half4 _TestColor;
                half4 _TestVector;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings UnlitVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {

                float3 color = _BaseColor.rgb * _TestFloat.xxx;
                return half4(saturate(color), 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
