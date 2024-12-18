Shader "Custom SRP/Post Process"
{
    Properties
    {
        [NoScaleOffset] _Lut ("LUT", 3D) = ""
        [NoScaleOffset] _RedBlueGradient ("Red Blue Gradient", 2D) = ""
        [NoScaleOffset] _YellowGreenGradient ("Yellow Green Gradient", 2D) = ""
        _MainTex ("Main Texture", 2D) = ""
    }
    SubShader
    {
        Pass
        {
            Cull Off
            ZTest Always
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment

            #include "Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            sampler2D _MainTex;
            sampler3D _Lut;
            sampler2D _RedBlueGradient;
            sampler2D _YellowGreenGradient;

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
                float3 src = tex2D(_MainTex, input.uv);
                if (input.uv.y > .5) return SRGBToLinear(src);

                src += .2;
                // src = float3(input.uv.xy, 0);

                // src *= LinearToSRGB(tex2D(_RedBlueGradient, input.uv.y));

                src.y = 1 - src.y;
                float3 dst = tex3D(_Lut, src);

                // dst *= tex2D(_YellowGreenGradient, input.uv.x);
                dst = lerp(dst, dst * tex2D(_RedBlueGradient, 1 - input.uv.y), 1 - Luminance(dst));

                return dst;
            }
            ENDHLSL
        }
    }
}
