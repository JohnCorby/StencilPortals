Shader "Custom SRP/Lit"
{
    Properties {}
    SubShader
    {
        Pass
        {
            Tags
            {
                "LightMode" = "CustomLit"
            }

            HLSLPROGRAM
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            #include "Common.hlsl"
            #include "URP.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            float3 _DirectionalLightColor;
            float3 _DirectionalLightDirection;
            float3 _AmbientLightColor;

            sampler2D _ShadowBuffer;
            float4x4 _ShadowMatrix;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : normalWS;
                float3 positionVS : positionWS;
                float4 positionLightSpace : positionLightSpace;
            };

            struct FragmentOutput
            {
                float3 color : SV_Target0;
                float3 normal : SV_Target1;
                float distance : SV_Target2;
            };

            Varyings LitPassVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS, true);
                output.positionVS = TransformWorldToView(TransformObjectToWorld(input.positionOS));
                output.positionLightSpace = mul(_ShadowMatrix, float4(input.positionOS, 1));
                return output;
            }

            FragmentOutput LitPassFragment(Varyings input)
            {
                FragmentOutput output;

                // float3 ramp = saturate(dot(input.normalWS, _DirectionalLightDirection) * .5 + .5) * _DirectionalLightColor;
                // return ramp;

                {
                    float3 projCoords = input.positionLightSpace.xyz / input.positionLightSpace.w;
                    projCoords = projCoords * 0.5 + 0.5;

                    float bias = 0.002;
                    projCoords.z -= bias;

                    float closestDepth = tex2D(_ShadowBuffer, projCoords.xy);
                    output.color = closestDepth.xxx;
                }

                float3 diffuse = saturate(dot(input.normalWS, _DirectionalLightDirection)) * _DirectionalLightColor / PI;
                float3 ambient = _AmbientLightColor;
                output.color = diffuse + ambient;

                output.normal = input.normalWS;

                output.distance = length(input.positionVS);

                output.color = lerp(output.color, LinearToSRGB(unity_FogColor), GetFogAmount(output.distance, false));

                return output;
            }
            ENDHLSL
        }
    }
}
