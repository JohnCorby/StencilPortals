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

            float3 _DirectionalLightColor;
            float3 _DirectionalLightDirection;
            float3 _AmbientLightColor;

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
            };

            struct FragmentOutput
            {
                float3 color : SV_Target0;
                float3 normalVS : SV_Target1;
                float customDepth : SV_Target2;
            };

            Varyings LitPassVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS, true);
                output.positionVS = TransformWorldToView(TransformObjectToWorld(input.positionOS));
                return output;
            }

            FragmentOutput LitPassFragment(Varyings input)
            {
                FragmentOutput output;

                // float3 ramp = saturate(dot(input.normalWS, _DirectionalLightDirection) * .5 + .5) * _DirectionalLightColor;
                // return ramp;

                float3 diffuse = saturate(dot(input.normalWS, _DirectionalLightDirection)) * _DirectionalLightColor / PI;
                float3 ambient = _AmbientLightColor;
                output.color = diffuse + ambient;

                output.normalVS = TransformWorldToViewNormal(input.normalWS, true);

                output.customDepth = length(input.positionVS); // use distance from camera instead of depth

                return output;
            }
            ENDHLSL
        }
    }
}
