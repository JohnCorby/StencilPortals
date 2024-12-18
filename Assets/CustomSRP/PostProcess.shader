Shader "Custom SRP/Post Process"
{
    Properties
    {
        [NoScaleOffset] _Lut ("LUT", 3D) = ""
        [NoScaleOffset] _RedBlueGradient ("Red Blue Gradient", 2D) = ""
        [NoScaleOffset] _YellowGreenGradient ("Yellow Green Gradient", 2D) = ""
        _MainTex ("Main Texture", 2D) = ""
    }
    SubShader
    {
        Pass
        {
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            sampler2D _MainTex;
            sampler3D _Lut;
            sampler2D _RedBlueGradient;
            sampler2D _YellowGreenGradient;

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

            Varyings UnlitPassVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            float3 UnlitPassFragment(Varyings input) : SV_Target
            {
                float3 src = tex2D(_MainTex, input.uv);
                // src = float3(input.uv.xy, 0);

                // src *= LinearToSRGB(tex2D(_RedBlueGradient, input.uv.y));

                src.y = 1 - src.y;
                float3 dst = tex3D(_Lut, src);
                // dst = src;

                // dst *= tex2D(_YellowGreenGradient, input.uv.x);
                // dst = lerp(dst, dst * tex2D(_RedBlueGradient, input.uv.y), 1 - Luminance(dst));

                return dst;
            }
            ENDHLSL
        }
    }
}
