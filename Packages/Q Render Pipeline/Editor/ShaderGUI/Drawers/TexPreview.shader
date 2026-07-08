Shader "Hidden/QRP/Editor/TexPreview"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _ChannelMask ("Channel Mask", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Tint;
            float4 _ChannelMask;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.uv) * _Tint;
                float4 mask = step(0.5, _ChannelMask);

                if (dot(mask, 1.0) < 0.5)
                    return color;

                if (mask.w > 0.5 && dot(mask.rgb, 1.0) < 0.5)
                    return fixed4(color.a, color.a, color.a, 1);

                fixed4 outputColor = fixed4(color.rgb * mask.rgb, 1);
                if (mask.w > 0.5)
                    outputColor.a = color.a;

                return outputColor;
            }
            ENDCG
        }
    }
}
