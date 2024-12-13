Shader "Custom SRP/Unlit"
{
    Properties {}
    SubShader
    {
        Pass
        {
            Tags
            {
                "LightMode" = "CustomUnlit"
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

            float4 UnlitPassFragment(float4 positionCS : SV_POSITION) : SV_Target
            {
                return float4(positionCS.z, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
