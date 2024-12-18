Shader "Custom SRP/test"
{
    Properties
    {
        [NoScaleOffset] _InputTex ("test input", 2D) = ""
        [NoScaleOffset] _OutputTex ("test output", 2D) = ""
    }
    SubShader
    {
        Pass
        {
            Tags
            {
                "LightMode" = "CustomLit"
            }

            HLSLPROGRAM
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment

            #include "Common.hlsl"

            sampler2D _InputTex;
            sampler2D _OutputTex;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : uv;
            };

            Varyings UnlitPassVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            float3 UnlitPassFragment(Varyings input) : SV_Target
            {
                return input.uv.x < .5 ? tex2D(_InputTex, input.uv) : tex2D(_OutputTex, input.uv);
            }
            ENDHLSL
        }
    }
}
