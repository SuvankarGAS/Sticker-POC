Shader "Effects/Doodle Sticker"
{
    Properties
    {
        [HideInInspector][NoScaleOffset] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HideInInspector][NoScaleOffset] _AlphaTex ("External Alpha", 2D) = "white" {}
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

        [MaterialToggle] PixelSnap ("Pixel Snap", Float) = 0
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

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "DoodleSticker"

            CGPROGRAM
            #pragma vertex DoodleStickerSpriteVertex
            #pragma fragment DoodleStickerSpriteFragment
            #pragma target 3.0
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile_local _ _DOODLE_STICKER_EXTERNAL_ALPHA
            #pragma shader_feature_local _DOODLE_STICKER_UNSCALED_TIME
            #pragma shader_feature_local _DOODLE_STICKER_DROP_SHADOW
            #pragma shader_feature_local _DOODLE_STICKER_BACKFACE_TEXTURE

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _AlphaTex;
            fixed4 _Color;

            fixed4 SampleDoodleStickerSpriteTexture(float2 atlasUV, float2 atlasUVDdx, float2 atlasUVDdy)
            {
                fixed4 textureColor = tex2Dgrad(_MainTex, atlasUV, atlasUVDdx, atlasUVDdy);
                #ifdef _DOODLE_STICKER_EXTERNAL_ALPHA
                textureColor.a = tex2Dgrad(_AlphaTex, atlasUV, atlasUVDdx, atlasUVDdy).r;
                #endif
                return textureColor;
            }

            #define DOODLE_STICKER_SAMPLE_ART(atlasUV, atlasUVDdx, atlasUVDdy) SampleDoodleStickerSpriteTexture(atlasUV, atlasUVDdx, atlasUVDdy)
            #include "DoodleStickerCore.cginc"

            struct DoodleStickerSpriteAttributes
            {
                float4 positionOS : POSITION;
                fixed4 vertexColor : COLOR;
                float2 stickerUV : TEXCOORD0;
                float4 atlasUVRect : TEXCOORD1;
                float4 pageCurlPoints : TEXCOORD2;
                float2 curlSpaceScale : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DoodleStickerSpriteVaryings
            {
                float4 positionCS : SV_POSITION;
                fixed4 tint : COLOR;
                float2 stickerUV : TEXCOORD0;
                float4 atlasUVRect : TEXCOORD1;
                float4 pageCurlPoints : TEXCOORD2;
                float2 curlSpaceScale : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DoodleStickerSpriteVaryings DoodleStickerSpriteVertex(DoodleStickerSpriteAttributes input)
            {
                DoodleStickerSpriteVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = UnityObjectToClipPos(input.positionOS);
                #ifdef PIXELSNAP_ON
                output.positionCS = UnityPixelSnap(output.positionCS);
                #endif

                output.tint = input.vertexColor * _Color;
                output.stickerUV = input.stickerUV;
                output.atlasUVRect = input.atlasUVRect;
                output.pageCurlPoints = input.pageCurlPoints;
                output.curlSpaceScale = input.curlSpaceScale;
                return output;
            }

            fixed4 DoodleStickerSpriteFragment(DoodleStickerSpriteVaryings input) : SV_Target
            {
                DoodleStickerSurface surface;
                surface.stickerUV = input.stickerUV;
                surface.atlasUVRect = input.atlasUVRect;
                surface.curlSpaceScale = input.curlSpaceScale;
                surface.grabPoint = input.pageCurlPoints.xy;
                surface.dragPoint = input.pageCurlPoints.zw;
                surface.tint = input.tint;
                return EvaluateDoodleSticker(surface);
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
    CustomEditor "DoodleStickers.EditorTooling.DoodleStickerShaderGUI"
}
