Shader "Custom SRP/Post Process"
{
    Properties
    {
        [HideInInspector] _MainTex ("Main Texture", 2D) = ""

        [NoScaleOffset] _Lut ("LUT", 3D) = ""
        [NoScaleOffset] _RedBlueGradient ("Red Blue Gradient", 2D) = ""
        [NoScaleOffset] _YellowGreenGradient ("Yellow Green Gradient", 2D) = ""

        [NoScaleOffset] _TestInput ("test input", 2D) = ""
        [NoScaleOffset] _TestOutput ("test output", 2D) = ""
        _TestSlider ("test slider", Vector) = (0,0,0,0)
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

            float3 _AmbientLightColor;

            sampler2D _TestInput;
            sampler2D _TestOutput;
            float4 _TestSlider;

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
                {
                    float2 uv = input.uv;
                    uv.y = 1 - uv.y;
                    float3 src = uv.x < _TestSlider.x ? tex2D(_TestInput, uv) : tex2D(_TestOutput, uv);
                    if (uv.y < _TestSlider.y)
                        return SRGBToLinear(src);
                }

                float3 src = tex2D(_MainTex, input.uv);

                src += _AmbientLightColor;
                // src = float3(input.uv.xy, 0);

                if (input.uv.x < _TestSlider.x)
                    return SRGBToLinear(src);

                // src *= LinearToSRGB(tex2D(_RedBlueGradient, input.uv.y));

                // src.y = 1 - src.y;
                float3 dst = tex3D(_Lut, src);

                // dst = lerp(dst, dst * tex2D(_YellowGreenGradient, input.uv), 1 - Luminance(dst));
                dst = lerp(dst, dst * tex2D(_RedBlueGradient, input.uv), 1 - Luminance(dst));

                return dst;
            }
            ENDHLSL
        }
    }
}
