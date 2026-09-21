// URP 전체화면 패스용. 깊이로 픽셀의 월드 좌표를 복원하고
// 월드 XZ를 _BW_VisionBounds로 0..1 UV로 바꿔 마스크와 기억을 읽는다.
// 보이는 곳은 원색, 기억한 곳은 어둡고 푸르게, 못 본 곳은 검정.
Shader "ByAWhisker/VisionComposite"
{
    Properties
    {
        _MemoryBrightness ("Memory Brightness", Range(0, 1)) = 0.22
        _MemoryTint ("Memory Tint", Color) = (0.55, 0.72, 1.0, 1.0)
        _MemoryDesaturation ("Memory Desaturation", Range(0, 1)) = 0.5
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.5)) = 0.15
        _UnseenColor ("Unseen Color", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "VisionComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeFrag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            // Vert, Varyings, _BlitTexture, sampler_LinearClamp을 준다.
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // VisibilityMaskRenderer가 전역으로 넣는 값이다.
            TEXTURE2D(_BW_VisionMask);
            TEXTURE2D(_BW_VisionMemory);
            float4 _BW_VisionBounds;   // (minX, minZ, sizeX, sizeZ)

            CBUFFER_START(UnityPerMaterial)
                float _MemoryBrightness;
                float4 _MemoryTint;
                float _MemoryDesaturation;
                float _EdgeSoftness;
                float4 _UnseenColor;
            CBUFFER_END

            half4 CompositeFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

                float rawDepth = SampleSceneDepth(uv);

                // 아무것도 그려지지 않은 하늘은 못 본 곳으로 친다.
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 0.0;
                    float deviceDepth = rawDepth;
                #else
                    bool isSky = rawDepth >= 1.0;
                    float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif

                float3 worldPos = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);

                // 월드 XZ를 마스크 UV로 바꾼다.
                float2 maskUV = (worldPos.xz - _BW_VisionBounds.xy) / max(_BW_VisionBounds.zw, 0.0001);
                bool inside = all(maskUV == saturate(maskUV)) && !isSky;

                half maskValue = 0.0h;
                half memoryValue = 0.0h;
                if (inside)
                {
                    maskValue = SAMPLE_TEXTURE2D(_BW_VisionMask, sampler_LinearClamp, maskUV).r;
                    memoryValue = SAMPLE_TEXTURE2D(_BW_VisionMemory, sampler_LinearClamp, maskUV).r;
                }

                // 경계는 부드럽게 한다.
                float soft = max(_EdgeSoftness, 0.001);
                half visible = smoothstep(0.5 - soft, 0.5 + soft, maskValue);
                half remembered = smoothstep(0.5 - soft, 0.5 + soft, memoryValue);
                remembered = max(remembered, visible);

                // 기억한 곳: 채도를 낮추고 어둡게, 푸른 색조를 입힌다.
                half luma = Luminance(sceneColor.rgb);
                half3 cooled = lerp(sceneColor.rgb, luma.xxx, _MemoryDesaturation);
                half3 memoryColor = cooled * _MemoryBrightness * _MemoryTint.rgb;

                half3 result = lerp(_UnseenColor.rgb, memoryColor, remembered);
                result = lerp(result, sceneColor.rgb, visible);

                return half4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
