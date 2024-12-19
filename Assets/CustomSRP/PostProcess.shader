Shader "Custom SRP/Post Process"
{
    Properties
    {
        [NoScaleOffset] _Lut ("LUT", 3D) = ""
        [NoScaleOffset] _RedBlueGradient ("Red Blue Gradient", 2D) = ""
        [NoScaleOffset] _YellowGreenGradient ("Yellow Green Gradient", 2D) = ""
        _VignetteParams ("Vignette Params (intensity, roundness, smoothness)", Vector) = (0,0,0,0)

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

            sampler2D _ColorBuffer;
            sampler2D _NormalBuffer;
            sampler2D _DepthBuffer;

            sampler3D _Lut;
            sampler2D _RedBlueGradient;
            sampler2D _YellowGreenGradient;

            float3 _AmbientLightColor;
            float3 _VignetteParams;

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

            // stolen from URP
            half3 ApplyVignette(half3 input, float2 uv, float2 center, float intensity, float roundness, float smoothness, half3 color)
            {
                // center = UnityStereoTransformScreenSpaceTex(center);
                float2 dist = abs(uv - center) * intensity;

                dist.x *= roundness;
                float vfactor = pow(saturate(1.0 - dot(dist, dist)), smoothness);
                return input * lerp(color, (1.0).xxx, vfactor);
            }

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
                    float3 col = uv.x < _TestSlider.x ? tex2D(_TestInput, uv) : tex2D(_TestOutput, uv);
                    if (uv.y < _TestSlider.y)
                        return SRGBToLinear(col);
                }

                float3 col = input.uv.x > _TestSlider.z ? tex2D(_ColorBuffer, input.uv) : tex2D(_TestInput, float2(input.uv.x, 1-input.uv.y));

                // col = float3(input.uv.xy, 0);

                if (input.uv.x < _TestSlider.x)
                    return SRGBToLinear(col);

                // col *= LinearToSRGB(tex2D(_RedBlueGradient, input.uv.y));

                col = ApplyVignette(col, input.uv, .5, _VignetteParams.x,  _VignetteParams.y, _VignetteParams.z, 0);

                // col.y = 1 - col.y;
                col = tex3D(_Lut, col);

                // col = lerp(col, col * tex2D(_YellowGreenGradient, input.uv), 1 - Luminance(col));
                // col = lerp(col, col * tex2D(_RedBlueGradient, input.uv), 1 - Luminance(col));
                col = lerp(col, col * tex2D(_RedBlueGradient, input.uv) * tex2D(_YellowGreenGradient, input.uv), 1 - Luminance(col));

                return col;
            }
            ENDHLSL
        }
    }
}
