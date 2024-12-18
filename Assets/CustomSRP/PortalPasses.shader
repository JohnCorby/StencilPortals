Shader "Custom SRP/Portal Passes"
{
    Properties {}
    SubShader
    {
        //Cull Off
        HLSLINCLUDE
        #include "Common.hlsl"
        ENDHLSL

        // punch hole pass
        Pass
        {
            ZWrite Off
            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
                Pass IncrSat
            }

            HLSLPROGRAM
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment

            float4 UnlitPassVertex(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }

            float3 UnlitPassFragment() : SV_Target
            {
                // write skybox color
                return 1;
            }
            ENDHLSL
        }

        // write depth pass
        Pass
        {
            ZTest Always
            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
            }

            HLSLPROGRAM
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment

            float4 UnlitPassVertex(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }

            float UnlitPassFragment() : SV_Depth
            {
                // write only depth
                return 0;
            }
            ENDHLSL
        }

        // unpunch hole pass
        Pass
        {
            ZTest Always
            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
                Pass DecrSat
            }

            HLSLPROGRAM
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment

            float4 UnlitPassVertex(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }

            void UnlitPassFragment()
            {
                // write nothing
            }
            ENDHLSL
        }
    }
}
