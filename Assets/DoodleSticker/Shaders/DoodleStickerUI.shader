Shader "Effects/Doodle Sticker UI"
{
    Properties
    {
        [HideInInspector][PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StickerOpacity ("Sticker Opacity", Range(0, 1)) = 1

        _DoodleWobbleTiling ("Doodle Wobble Tiling", Range(2, 16)) = 7
        _DoodleWobbleStrength ("Doodle Wobble Strength", Range(0, 0.02)) = 0.0035
        _DoodleWobbleFramesPerSecond ("Doodle Wobble Frames Per Second", Range(0, 16)) = 0.5
        [Toggle(_DOODLE_STICKER_UNSCALED_TIME)] _DoodleWobbleUseUnscaledTime ("Doodle Wobble Uses Unscaled Time", Float) = 0

        _PageCurlRadius ("Page Curl Radius", Range(0.001, 0.5)) = 0.12
        _PageCurlShading ("Page Curl Shading", Range(0, 1)) = 0.6
        _PageCurlHighlightStrength ("Page Curl Highlight Strength", Range(0, 1)) = 0.35

        _PageCurlShadowColor ("Page Curl Shadow Color", Color) = (0,0,0,0.5)
        _PageCurlShadowSoftness ("Page Curl Shadow Softness", Range(0.001, 0.2)) = 0.03
        _PageCurlShadowOffset ("Page Curl Shadow Offset", Vector) = (0.015,-0.025,0,0)
        [Toggle(_DOODLE_STICKER_DROP_SHADOW)] _PageCurlDropShadow ("Page Curl Drop Shadow", Float) = 1

        _PageCurlBackfaceColor ("Page Curl Backface Color", Color) = (1,1,1,1)
        [Toggle(_DOODLE_STICKER_BACKFACE_TEXTURE)] _PageCurlUseBackfaceTexture ("Page Curl Use Backface Texture", Float) = 0
        _PageCurlBackfaceTexture ("Page Curl Backface Texture", 2D) = "white" {}
        _PageCurlArtBleed ("Page Curl Art Bleed", Range(0, 1)) = 0.08

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "DoodleStickerUI"

            CGPROGRAM
            #pragma vertex DoodleStickerGraphicVertex
            #pragma fragment DoodleStickerGraphicFragment
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma shader_feature_local _DOODLE_STICKER_UNSCALED_TIME
            #pragma shader_feature_local _DOODLE_STICKER_DROP_SHADOW
            #pragma shader_feature_local _DOODLE_STICKER_BACKFACE_TEXTURE

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;

            fixed4 SampleDoodleStickerGraphicTexture(float2 atlasUV, float2 atlasUVDdx, float2 atlasUVDdy)
            {
                return tex2Dgrad(_MainTex, atlasUV, atlasUVDdx, atlasUVDdy) + _TextureSampleAdd;
            }

            #define DOODLE_STICKER_SAMPLE_ART(atlasUV, atlasUVDdx, atlasUVDdy) SampleDoodleStickerGraphicTexture(atlasUV, atlasUVDdx, atlasUVDdy)
            #include "DoodleStickerCore.cginc"

            struct DoodleStickerGraphicAttributes
            {
                float4 positionOS : POSITION;
                fixed4 vertexColor : COLOR;
                float2 stickerUV : TEXCOORD0;
                float4 atlasUVRect : TEXCOORD1;
                float4 pageCurlPoints : TEXCOORD2;
                float2 curlSpaceScale : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DoodleStickerGraphicVaryings
            {
                float4 positionCS : SV_POSITION;
                fixed4 tint : COLOR;
                float2 stickerUV : TEXCOORD0;
                float4 atlasUVRect : TEXCOORD1;
                float4 pageCurlPoints : TEXCOORD2;
                float2 curlSpaceScale : TEXCOORD3;
                float4 clipRectMask : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DoodleStickerGraphicVaryings DoodleStickerGraphicVertex(DoodleStickerGraphicAttributes input)
            {
                DoodleStickerGraphicVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 positionCS = UnityObjectToClipPos(input.positionOS);
                output.positionCS = positionCS;

                float2 pixelSize = positionCS.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedClipRect = clamp(_ClipRect, -2e10, 2e10);
                output.clipRectMask = float4(
                    input.positionOS.xy * 2.0 - clampedClipRect.xy - clampedClipRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                output.tint = input.vertexColor * _Color;
                output.stickerUV = input.stickerUV;
                output.atlasUVRect = input.atlasUVRect;
                output.pageCurlPoints = input.pageCurlPoints;
                output.curlSpaceScale = input.curlSpaceScale;
                return output;
            }

            fixed4 DoodleStickerGraphicFragment(DoodleStickerGraphicVaryings input) : SV_Target
            {
                const half vertexAlphaPrecision = half(0xff);
                input.tint.a = round(input.tint.a * vertexAlphaPrecision) / vertexAlphaPrecision;

                DoodleStickerSurface surface;
                surface.stickerUV = input.stickerUV;
                surface.atlasUVRect = input.atlasUVRect;
                surface.curlSpaceScale = input.curlSpaceScale;
                surface.grabPoint = input.pageCurlPoints.xy;
                surface.dragPoint = input.pageCurlPoints.zw;
                surface.tint = input.tint;
                float4 stickerColor = EvaluateDoodleSticker(surface);

                #ifdef UNITY_UI_CLIP_RECT
                half2 clipRectCoverage = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.clipRectMask.xy)) * input.clipRectMask.zw);
                stickerColor *= clipRectCoverage.x * clipRectCoverage.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(stickerColor.a - 0.001);
                #endif

                return stickerColor;
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
    CustomEditor "DoodleStickers.EditorTooling.DoodleStickerShaderGUI"
}
