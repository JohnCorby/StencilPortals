﻿Shader "Custom SRP/Post Process"
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
            #include "URP.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            sampler2D _ColorBuffer;
            float4 _ColorBuffer_TexelSize;
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

            float4x4 _InvViewProj;

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

            // for now its the bad algorithm
            float Edge(float2 uv)
            {
                const float alpha = DegToRad(10);
                const float eps = .1;

                float2 pixel = uv;

                float sum = 0;
                for (int i = -1; i <= 1; i++)
                {
                    for (int j = -1; j <= 1; j++)
                    {
                        float2 n = pixel + float2(i,j)*_ColorBuffer_TexelSize.xy;

                        float3 normal_n = tex2D(_NormalBuffer, n);
                        float3 normal_pixel = tex2D(_NormalBuffer, pixel);
                        float3 world_position_n = ComputeWorldSpacePosition(n, tex2D(_DepthBuffer, n), _InvViewProj);
                        float3 world_position_pixel = ComputeWorldSpacePosition(pixel, tex2D(_DepthBuffer, pixel), _InvViewProj);

                        float normalDist = dot(normal_n, normal_pixel);
                        float planeDistance = abs(dot(normal_pixel, world_position_n - world_position_pixel));

                        if (normalDist < cos(alpha) || planeDistance > eps)
                        {
                            // return 1;
                            sum++;
                        }
                    }
                }

                // return 0;
                return sum/9;
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

                col *= 1-Edge(input.uv);

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
