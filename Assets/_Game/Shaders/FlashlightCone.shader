// 경비 손전등의 바닥 부채꼴. 정점 색을 그대로 반투명으로 그리되,
// 플레이어가 지금 보고 있는 곳(_BW_VisionMask)에서만 보이게 자른다.
//
// 부채꼴은 어둠을 칠하는 합성 패스 뒤(반투명 큐)에 그려서 어둠에 묻히지 않는다. 그 덕에 판정 경계가
// 또렷하지만, 자르지 않으면 못 보는 곳의 빛까지 떠서 벽 너머 경비의 자리를 전부 알려 준다.
// 합성 패스와 같은 마스크, 같은 경계 부드러움을 써서 시야 끝에서 빛과 어둠이 같은 선에서 갈린다.
Shader "ByAWhisker/FlashlightCone"
{
    Properties
    {
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.5)) = 0.15
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        // 시야 마스크는 여기 선언하지 않는다. Properties에 넣으면 전역 텍스처를 가린다.
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest [_ZTest]
        Cull Off

        Pass
        {
            Name "FlashlightCone"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // VisibilityMaskRenderer가 전역으로 넣는 값이다.
            TEXTURE2D(_BW_VisionMask);
            SAMPLER(sampler_BW_VisionMask);
            float4 _BW_VisionBounds;   // (minX, minZ, sizeX, sizeZ)

            CBUFFER_START(UnityPerMaterial)
                float _EdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 maskUV = (input.positionWS.xz - _BW_VisionBounds.xy) / max(_BW_VisionBounds.zw, 0.0001);

                // 마스크 밖은 못 본 곳이다. 합성 패스도 그렇게 친다.
                half visible = 0.0h;
                if (all(maskUV == saturate(maskUV)))
                {
                    half maskValue = SAMPLE_TEXTURE2D(_BW_VisionMask, sampler_BW_VisionMask, maskUV).r;
                    float soft = max(_EdgeSoftness, 0.001);
                    visible = smoothstep(0.5 - soft, 0.5 + soft, maskValue);
                }

                half4 color = input.color;
                color.a *= visible;
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
