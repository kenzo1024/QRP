Shader "QRP/Unlit"
{
    Properties
    {
        [MatDataTransfer][MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MatDataTransfer] _TestFloat("Test RGB Brightness", Range(-1.0, 1.0)) = 0.0
        [MatDataTransfer] _TestFloat0("Test Float 0", Float) = 0.0
        [MatDataTransfer] _TestFloat1("Test Float 1", Float) = 0.0
        [MatDataTransfer] _TestFloat2("Test Float 2", Float) = 0.0
        [MatDataTransfer] _TestColor0("Test Color 0", Color) = (1, 1, 1, 1)
        [MatDataTransfer] _TestColor1("Test Color 1", Color) = (1, 1, 1, 1)
        [MatDataTransfer] _TestColor2("Test Color 2", Color) = (1, 1, 1, 1)
        [MatDataTransfer] _TestColor3("Test Color 3", Color) = (1, 1, 1, 1)
        [MatDataTransfer] _TestVector0("Test Vector 0", Vector) = (0, 0, 0, 0)
        [MatDataTransfer] _TestVector1("Test Vector 1", Vector) = (0, 0, 0, 0)
        [MatDataTransfer] _TestVector2("Test Vector 2", Vector) = (0, 0, 0, 0)
        [MatDataTransfer] _TestVector3("Test Vector 3", Vector) = (0, 0, 0, 0)
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
                half _TestFloat0;
                half _TestFloat1;
                half _TestFloat2;
                half4 _TestColor0;
                half4 _TestColor1;
                half4 _TestColor2;
                half4 _TestColor3;
                half4 _TestVector0;
                half4 _TestVector1;
                half4 _TestVector2;
                half4 _TestVector3;
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

                float brightness = _TestFloat + _TestFloat0 * 0.01h + _TestFloat1 * 0.01h + _TestFloat2 * 0.01h;
                float3 color = _BaseColor.rgb * brightness.xxx;
                return half4(saturate(color), 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
