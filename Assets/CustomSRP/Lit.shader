Shader "Custom SRP/Lit"
{
    Properties {}
    SubShader
    {
        HLSLINCLUDE
        #include "Common.hlsl"
        ENDHLSL

        Pass
        {
            Tags
            {
                "LightMode" = "CustomLit"
            }

            HLSLPROGRAM
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            float3 _DirectionalLightColor;
            float3 _DirectionalLightDirection;
            float3 _AmbientLightColor;

            // sampler2D _ShadowBuffer;
            TEXTURE2D_SHADOW(_ShadowBuffer);
            #define SHADOW_SAMPLER sampler_linear_clamp_compare
            SAMPLER_CMP(SHADOW_SAMPLER);
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
                float3 positionVS : positionVS;
                float3 positionLightSpace : positionLightSpace;
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
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionVS = TransformWorldToView(positionWS);
                output.positionLightSpace = mul(_ShadowMatrix, float4(positionWS, 1));
                // output.positionCS = mul(_ShadowMatrix, float4(positionWS, 1));;
                return output;
            }

            FragmentOutput LitPassFragment(Varyings input)
            {
                FragmentOutput output;

                // float3 ramp = saturate(dot(input.normalWS, _DirectionalLightDirection) * .5 + .5) * _DirectionalLightColor;
                // return ramp;

                bool shadow;
                {
                    float3 projCoords = input.positionLightSpace.xyz;
                    projCoords = projCoords * 0.5 + 0.5;

                    // float bias = 0.002;
                    // projCoords.z -= bias;

                    // float closestDepth = tex2D(_ShadowBuffer, projCoords.xy);
                    // float currentDepth = input.positionCS.z;
                    // shadow = currentDepth > closestDepth;
                    shadow = SAMPLE_TEXTURE2D_SHADOW(_ShadowBuffer, SHADOW_SAMPLER, projCoords);

                    // output.color = float3(currentDepth, closestDepth, shadow);
                    // output.color = shadow;
                }

                float3 diffuse = saturate(dot(input.normalWS, _DirectionalLightDirection) * shadow) * _DirectionalLightColor / PI;
                float3 ambient = _AmbientLightColor;
                output.color = diffuse * shadow + ambient;

                output.normal = input.normalWS;

                output.distance = length(input.positionVS);

                output.color = lerp(output.color, _FogColor, GetFogAmount(output.distance, false));

                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            HLSLPROGRAM
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment

            float4 UnlitPassVertex(float3 positionOS : POSITION) : SV_POSITION
            {
                float4 positionCS = TransformObjectToHClip(positionOS);

	            #if UNITY_REVERSED_Z
		            positionCS.z =
			            min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
	            #else
		            positionCS.z =
			            max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
	            #endif

                return positionCS;
            }

            void UnlitPassFragment()
            {
                // write depth
            }
            ENDHLSL
        }
    }
}
