Shader "Custom SRP/Post Process"
{
    Properties
    {
        [NoScaleOffset] _Lut ("LUT", 3D) = ""
        [NoScaleOffset] _RedBlueGradient ("Red Blue Gradient", 2D) = ""
        [NoScaleOffset] _YellowGreenGradient ("Yellow Green Gradient", 2D) = ""
        _VignetteParams ("Vignette Params (intensity, roundness, smoothness)", Vector) = (0,0,0,0)
        _OverlayIntensity ("Overlay Intensity", Float) = 1

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
            #pragma target 4.5
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment

            #include "Common.hlsl"
            #include "URP.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            sampler2D _ColorBuffer;
            float4 _ColorBuffer_TexelSize;
            Texture2DMS<float3> _NormalBuffer;
            Texture2DMS<float> _DistanceBuffer;

            sampler3D _Lut;
            sampler2D _RedBlueGradient;
            sampler2D _YellowGreenGradient;

            float3 _VignetteParams;
            float _OverlayIntensity;

            sampler2D _TestInput;
            sampler2D _TestOutput;
            float4 _TestSlider;

            float3 _AmbientLightColor;
            // TL, TR, BL, BR
            float3 _CameraCorners[4];

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

            float4x4 _ViewMatrix;

            float3 GetWorldPos(float2 uv, float distance)
            {
                float3 top = lerp(_CameraCorners[0], _CameraCorners[1], uv.x);
                float3 bottom = lerp(_CameraCorners[2], _CameraCorners[3], uv.x);
                float3 ray = lerp(bottom, top, uv.y);

                float3 worldPos = normalize(ray) * distance;
                return mul(_ViewMatrix, float4(worldPos, 1));
            }

            // return: edge amount, min distance
            float2 GetEdgeData(float2 uv)
            {
                const float alpha = DegToRad(15);
                const float eps = 1.0 / 15;

                float2 pixel = uv;
                float3 normal_pixel = _NormalBuffer.Load(pixel * _ColorBuffer_TexelSize.zw, 0);
                if (all(normal_pixel == 0)) return 0;
                float distance_pixel = _DistanceBuffer.Load(pixel * _ColorBuffer_TexelSize.zw, 0);
                float3 world_position_pixel = GetWorldPos(pixel, distance_pixel);

                const int NUM_SAMPLES = 8;
                const int NUM_OFFSETS = 4;
                const int2 offsets[NUM_OFFSETS] = {
                    int2(0, 1),
                    int2(0, -1),
                    int2(1, 0),
                    int2(-1, 0),
                    // int2(-1, -1),
                    // int2(1, 1),
                    // int2(-1, 1),
                    // int2(1, -1),
                };

                float edgeMean = 0;
                float minDistance = 99999;
                for (int i = 0; i < NUM_OFFSETS; i++)
                {
                    float2 n = pixel + offsets[i] * _ColorBuffer_TexelSize.xy;

                    float distanceMean = 0;
                    for (int sample = 0; sample < NUM_SAMPLES; sample++)
                    {
                        float3 normal_n = _NormalBuffer.Load(n * _ColorBuffer_TexelSize.zw, sample);
                        float distance_n = _DistanceBuffer.Load(n * _ColorBuffer_TexelSize.zw, sample);
                        float3 world_position_n = GetWorldPos(n, distance_n);

                        float normalDist = dot(normal_n, normal_pixel);
                        float planeDistance = abs(dot(normal_pixel, world_position_n - world_position_pixel));

                        if (normalDist < cos(alpha) || planeDistance > eps)
                        {
                            edgeMean++;
                        }

                        distanceMean += distance_n;
                    }
                    distanceMean /= NUM_SAMPLES;

                    minDistance = min(minDistance, distanceMean);
                }

                float edgeAmount = saturate(edgeMean / (NUM_OFFSETS * NUM_SAMPLES) * 2);
                return float2(edgeAmount, minDistance);
            }

            Varyings UnlitPassVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            sampler2D _ShadowBuffer;

            float3 UnlitPassFragment(Varyings input) : SV_Target
            {
                if (false)
                {
                    float3 worldPos = GetWorldPos(input.uv, _DistanceBuffer.Load(input.uv * _ColorBuffer_TexelSize.zw, 0));

                    // The following part creates the checkerboard effect.
                    // Scale is the inverse size of the squares.
                    float scale = .1;
                    // Scale, mirror and snap the coordinates.
                    uint3 worldIntPos = uint3(abs(worldPos.xyz * scale));
                    // Divide the surface into squares. Calculate the color ID value.
                    bool white = ((worldIntPos.x) & 1) ^ (worldIntPos.y & 1) ^ (worldIntPos.z & 1);
                    // Color the square based on the ID value (black or white).
                    half4 color = white ? half4(1, 1, 1, 1) : half4(0, 0, 0, 1);
                    return color;
                }

                // if (all(input.uv < 1 / 3.)) return tex2D(_ShadowBuffer, input.uv * 3);
                if (all(input.uv < 1 / 3.)) return _NormalBuffer.Load(input.uv * 3 * _ColorBuffer_TexelSize.zw, 0);

                {
                    float2 uv = input.uv;
                    uv.y = 1 - uv.y;
                    float3 col = uv.x < _TestSlider.x ? tex2D(_TestInput, uv) : tex2D(_TestOutput, uv);
                    if (uv.y < _TestSlider.y)
                        return SRGBToLinear(col);
                }

                float3 col = input.uv.x > _TestSlider.z ? tex2D(_ColorBuffer, input.uv) : tex2D(_TestInput, float2(input.uv.x, 1 - input.uv.y));

                // col = float3(input.uv.xy, 0);

                if (input.uv.x < _TestSlider.x)
                    return SRGBToLinear(col);

                // col *= LinearToSRGB(tex2D(_RedBlueGradient, input.uv.y));

                {
                    float2 edgeData = GetEdgeData(input.uv);
                    col = lerp(col, lerp(0, _FogColor, 0), edgeData.x);
                }

                col = ApplyVignette(col, input.uv, .5, _VignetteParams.x, _VignetteParams.y, _VignetteParams.z, 0);

                // col.y = 1 - col.y;
                col = tex3D(_Lut, col);

                col = lerp(col, col * tex2D(_RedBlueGradient, input.uv) * _OverlayIntensity, 1 - Luminance(col));
                col = lerp(col, col * tex2D(_YellowGreenGradient, input.uv) * _OverlayIntensity, 1 - Luminance(col));
                // col = lerp(col, col * tex2D(_RedBlueGradient, input.uv) * _OverlayIntensity * tex2D(_YellowGreenGradient, input.uv) * _OverlayIntensity, 1 - Luminance(col));

                return col;
            }
            ENDHLSL
        }
    }
}
