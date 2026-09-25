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
        // 냄새 텍스처는 여기 선언하지 않는다. Properties에 넣으면 같은 이름의 전역 텍스처를
        // 가려서 전역으로 넣은 지도가 아예 안 들어온다. 숫자만 머티리얼에서 뺀다.
        _ScentGlow ("Scent Glow", Range(0, 4)) = 1.2
        // 드러냄 값(_BW_ScentReveal)도 여기 선언하지 않는다. _ScentGlow와 달리 이건 FocusSense가
        // 매 프레임 전역으로 넣는 값이라, Properties에 두면 머티리얼 값이 전역을 가려서 집중과
        // 상관없이 늘 같은 세기로 뜬다. _BW_PrevMemory 때 겪은 그 사고다.
        _ScentGamma ("Scent Gamma", Range(0.2, 2)) = 0.65
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

            // ScentMapRenderer가 전역으로 넣는 값이다.
            // rgb = 주인 색 x 신선도(갓 남은 것 1, 오래된 것 0), a = 세기 0..1.
            TEXTURE2D(_BW_ScentMap);
            float4 _BW_ScentBounds;    // (minX, minZ, sizeX, sizeZ). 시야 범위와 다를 수 있다.

            // FocusSense가 전역으로 넣는 값이다. 0..1, 집중하는 동안만 1로 오른다.
            // 아무도 넣지 않으면 0으로 읽히고 자취는 아예 안 뜬다. 그게 맞는 기본값이다 —
            // 집중하지 않는데 자취가 보이는 쪽이 냄새를 감각이 아니라 배경으로 만든다.
            float _BW_ScentReveal;

            CBUFFER_START(UnityPerMaterial)
                float _MemoryBrightness;
                float4 _MemoryTint;
                float _MemoryDesaturation;
                float _EdgeSoftness;
                float4 _UnseenColor;
                float _ScentGlow;
                float _ScentGamma;
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

                // 자취는 눈이 닿지 않은 곳에서도 떠야 한다. 그게 코를 쓰는 값어치다.
                // 그래서 visible/remembered로 자르지 않고, 어둡게 만드는 계산이 다 끝난 뒤에 얹는다.
                //
                // 다만 집중하는 동안만 얹는다. 0일 때 아예 건너뛰는 것은 비용 때문만이 아니라,
                // 집중하지 않을 때 화면이 M9 이전과 한 픽셀도 다르지 않다는 것을 이 분기가 보장하기 때문이다.
                half reveal = saturate(_BW_ScentReveal);
                if (!isSky && reveal > 0.0h)
                {
                    // 냄새 격자는 시야 마스크와 범위가 다를 수 있어 UV를 따로 만든다.
                    float2 scentUV = (worldPos.xz - _BW_ScentBounds.xy) / max(_BW_ScentBounds.zw, 0.0001);
                    if (all(scentUV == saturate(scentUV)))
                    {
                        half4 scent = SAMPLE_TEXTURE2D(_BW_ScentMap, sampler_LinearClamp, scentUV);

                        // 세기에 1보다 작은 감마를 먹인다. 상한 대신 감마를 고른 이유:
                        // _ScentGlow를 2.5에서 1.2로 내리면 모든 세기가 같은 비율로 내려가서
                        // 짙은 심지는 알맞아지지만 옅은 가장자리가 검정에 묻혀 버린다. 자취에서
                        // 방향을 읽게 해 주는 것은 바로 그 옅은 꼬리다. 상한은 짙은 쪽만 자를 뿐
                        // 묻힌 꼬리를 되살리지 못한다. 감마는 약한 값을 더 많이 끌어올리고
                        // 1 근처는 그대로 두므로, 전체를 누르면서 꼬리만 살려 낸다.
                        // rgb와 세기가 모두 0..1이라 얹는 양의 상한은 _ScentGlow로 이미 묶여 있다.
                        half shaped = pow(max(scent.a, 0.0h), max(_ScentGamma, 0.01h));
                        half3 trail = scent.rgb * shaped * _ScentGlow * reveal;

                        // 그냥 더하면 밝은 곳에서 하얗게 타서 주인 색이 날아간다.
                        // 화면이 밝을수록 덜 얹어, 검은 화면(못 본 곳·벽 뒤)에서 가장 또렷하게 뜨게 한다.
                        // 밝은 곳은 어차피 눈으로 보이니 자취가 약해도 손해가 없다.
                        half shade = 1.0h - saturate(Luminance(result));
                        result += trail * shade;
                    }
                }

                return half4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
