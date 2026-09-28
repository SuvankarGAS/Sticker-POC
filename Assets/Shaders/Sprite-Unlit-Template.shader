// Production template for SpriteRenderer (Built-in Render Pipeline).
// Mirrors Unity's Sprites-Default contract: SpriteRenderer color/flip, instancing,
// sprite atlases, pixel snap, ETC1 split alpha, premultiplied-alpha output.
Shader "Custom/Sprites/Unlit Template"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0

        // Example optional feature. shader_feature_local = only variants used by materials get built.
        [Toggle(_GRAYSCALE_ON)] _Grayscale ("Grayscale", Float) = 0
        _GrayAmount ("Gray Amount", Range(0,1)) = 1

        // Set by SpriteRenderer, never by hand.
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
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
        Blend One OneMinusSrcAlpha   // premultiplied alpha (see frag)

        Pass
        {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #pragma shader_feature_local _GRAYSCALE_ON

            // Declares _MainTex, _AlphaTex, _Color, _RendererColor, _Flip (instancing-aware),
            // plus SampleSpriteTexture() and UnityFlipSprite().
            #include "UnitySprites.cginc"

            half _GrayAmount;

            struct Attributes
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float4 pos = UnityFlipSprite(IN.vertex.xyz, _Flip.xy);
                OUT.vertex = UnityObjectToClipPos(pos);
                OUT.texcoord = IN.texcoord;
                // Fold all constant color math into the vertex stage.
                OUT.color = IN.color * _Color * _RendererColor;

                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Handles ETC1 external alpha automatically.
                half4 c = SampleSpriteTexture(IN.texcoord) * IN.color;

                #ifdef _GRAYSCALE_ON
                half luma = dot(c.rgb, half3(0.2126, 0.7152, 0.0722));
                c.rgb = lerp(c.rgb, luma.xxx, _GrayAmount);
                #endif

                // ---- your effect goes here (work in straight alpha) ----

                c.rgb *= c.a; // premultiply last
                return c;
            }
        ENDCG
        }
    }

    Fallback "Sprites/Default"
}
