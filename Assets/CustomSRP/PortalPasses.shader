﻿Shader "Custom SRP/Portal Passes"
{
    Properties {}
    SubShader
    {
        //Cull Off
        HLSLINCLUDE
        #include "Common.hlsl"
        ENDHLSL

        Pass
        {
            Name "punch hole"
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

            void UnlitPassFragment()
            {
                // read depth, write stencil
            }
            ENDHLSL
        }

        Pass
        {
            Name "clear"
            ZTest Always
            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
            }

            HLSLPROGRAM
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment

            struct FragmentOutput
            {
                float3 color : SV_Target0;
                float3 normalVS : SV_Target1;
                float customDepth : SV_Target2;
                float depth : SV_Depth;
            };

            float4 UnlitPassVertex(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }

            FragmentOutput UnlitPassFragment()
            {
                // read stencil, write targets and depth
                FragmentOutput output;
                output.color = 1;
                output.normalVS = 1;
                output.customDepth = 1;
                output.depth = 0;
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "unpunch hole"
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
                // read stencil, write stencil and depth
            }
            ENDHLSL
        }
    }
}
