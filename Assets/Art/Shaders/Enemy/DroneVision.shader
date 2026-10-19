Shader "MyPrecious/DroneVision"
{
    Properties
    {
        [MainColor] _BaseColor("底色", Color) = (1, 0.67, 0.1, 0.2)
        _FillColor("填充色", Color) = (1, 0.22, 0.12, 0.24)
        _FillProgress("填充进度", Range(0, 1)) = 0
        [HideInInspector] _Cull("背面剔除", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _FillColor;
                float _FillProgress;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // UV.x从机头0到可见外缘1；比较位置，产生红橙分界而非整体渐变。
                bool filled = _FillProgress >= 1 || (_FillProgress > 0 && input.uv.x <= _FillProgress);
                return filled ? _FillColor : _BaseColor;
            }
            ENDHLSL
        }
    }
}
