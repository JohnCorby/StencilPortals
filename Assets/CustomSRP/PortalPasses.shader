Shader "Custom SRP/Portal Passes"
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
            ColorMask 0
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

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct FragmentOutput
            {
                float3 color : SV_Target0;
                float3 normal : SV_Target1;
                float distance : SV_Target2;
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
                output.color = _FogColor;
                output.normal = float3(0, 0, 1);
                output.distance = _FogParams.w;
                output.depth = 0;
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "unpunch hole"
            ZTest Always
            ColorMask 0
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
