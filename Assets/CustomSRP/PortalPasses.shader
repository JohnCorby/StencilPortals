Shader "Custom SRP/Portal Passes"
{
    Properties
    {
        _StencilRef ("Stencil Ref", Range(0, 255)) = 0
    }
    SubShader
    {
        // punch hole pass
        Pass
        {
            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
                Pass IncrSat
            }

            HLSLPROGRAM
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment

            #define UNITY_MATRIX_M unity_ObjectToWorld
            #define UNITY_MATRIX_I_M unity_WorldToObject
            #define UNITY_MATRIX_V unity_MatrixV
            #define UNITY_MATRIX_I_V unity_MatrixInvV
            #define UNITY_MATRIX_VP unity_MatrixVP
            #define UNITY_PREV_MATRIX_M unity_prev_MatrixM
            #define UNITY_PREV_MATRIX_I_M unity_prev_MatrixIM
            #define UNITY_MATRIX_P glstate_matrix_projection

            float4x4 unity_ObjectToWorld;
            float4x4 unity_WorldToObject;

            float4x4 unity_MatrixVP;
            float4x4 unity_MatrixV;
            float4x4 unity_MatrixInvV;
            float4x4 unity_prev_MatrixM;
            float4x4 unity_prev_MatrixIM;
            float4x4 glstate_matrix_projection;
            float4 unity_WorldTransformParams;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            float4 UnlitPassVertex(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }

            float4 UnlitPassFragment() : SV_Target
            {
                return 0;
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

            #define UNITY_MATRIX_M unity_ObjectToWorld
            #define UNITY_MATRIX_I_M unity_WorldToObject
            #define UNITY_MATRIX_V unity_MatrixV
            #define UNITY_MATRIX_I_V unity_MatrixInvV
            #define UNITY_MATRIX_VP unity_MatrixVP
            #define UNITY_PREV_MATRIX_M unity_prev_MatrixM
            #define UNITY_PREV_MATRIX_I_M unity_prev_MatrixIM
            #define UNITY_MATRIX_P glstate_matrix_projection

            float4x4 unity_ObjectToWorld;
            float4x4 unity_WorldToObject;

            float4x4 unity_MatrixVP;
            float4x4 unity_MatrixV;
            float4x4 unity_MatrixInvV;
            float4x4 unity_prev_MatrixM;
            float4x4 unity_prev_MatrixIM;
            float4x4 glstate_matrix_projection;
            float4 unity_WorldTransformParams;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            float4 UnlitPassVertex(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }

            float UnlitPassFragment() : SV_Depth
            {
                return 0.003;
            }
            ENDHLSL
        }

        // unpunch hole pass
        Pass
        {
            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
                Pass DecrSat
            }

            HLSLPROGRAM
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment

            #define UNITY_MATRIX_M unity_ObjectToWorld
            #define UNITY_MATRIX_I_M unity_WorldToObject
            #define UNITY_MATRIX_V unity_MatrixV
            #define UNITY_MATRIX_I_V unity_MatrixInvV
            #define UNITY_MATRIX_VP unity_MatrixVP
            #define UNITY_PREV_MATRIX_M unity_prev_MatrixM
            #define UNITY_PREV_MATRIX_I_M unity_prev_MatrixIM
            #define UNITY_MATRIX_P glstate_matrix_projection

            float4x4 unity_ObjectToWorld;
            float4x4 unity_WorldToObject;

            float4x4 unity_MatrixVP;
            float4x4 unity_MatrixV;
            float4x4 unity_MatrixInvV;
            float4x4 unity_prev_MatrixM;
            float4x4 unity_prev_MatrixIM;
            float4x4 glstate_matrix_projection;
            float4 unity_WorldTransformParams;

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            float4 UnlitPassVertex(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }

            float4 UnlitPassFragment() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
