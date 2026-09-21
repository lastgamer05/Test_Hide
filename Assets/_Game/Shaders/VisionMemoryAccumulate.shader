// 시야 마스크를 그리고, 기억 텍스처에 최댓값으로 누적한다.
// 패스 0: 가시 폴리곤을 흰색으로 채운다 (VisibilityMaskRenderer의 PassMaskFill).
// 패스 1: max(현재 마스크, 이전 기억)을 출력하는 블릿 (PassMemoryMax).
Shader "Hidden/ByAWhisker/VisionMemoryAccumulate"
{
    // 프로퍼티를 두지 않는다. 머티리얼 프로퍼티는 전역 텍스처를 덮어써서
    // 이전 기억이 항상 검정으로 들어오기 때문이다. 값은 전역으로만 받는다.

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        // ---------------------------------------------------------------
        Pass
        {
            Name "VisionMaskFill"

            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct MaskAttributes
            {
                float4 positionOS : POSITION;
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            // 모델 행렬은 Translate(Origin), 뷰/투영은 위에서 내려다보는 직교 행렬이다.
            MaskVaryings MaskVert(MaskAttributes input)
            {
                MaskVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 MaskFrag(MaskVaryings input) : SV_Target
            {
                return half4(1.0h, 1.0h, 1.0h, 1.0h);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------
        Pass
        {
            Name "VisionMemoryMax"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment MemoryFrag
            #pragma target 3.0

            // Vert, Varyings, _BlitTexture, _BlitScaleBias, sampler_PointClamp을 준다.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_BW_PrevMemory);

            half4 MemoryFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                // 같은 해상도끼리 1:1 블릿이므로 점 샘플링을 쓴다.
                half current = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).r;
                half previous = SAMPLE_TEXTURE2D_LOD(_BW_PrevMemory, sampler_PointClamp, uv, 0).r;

                half accumulated = max(current, previous);
                return half4(accumulated, accumulated, accumulated, accumulated);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
