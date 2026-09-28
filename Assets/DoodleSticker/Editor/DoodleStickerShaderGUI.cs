using UnityEditor;
using UnityEngine;

namespace DoodleStickers.EditorTooling
{
    public sealed class DoodleStickerShaderGUI : ShaderGUI
    {
        private readonly struct PropertyDescription
        {
            public readonly string PropertyName;
            public readonly string Label;
            public readonly string Tooltip;

            public PropertyDescription(string propertyName, string label, string tooltip)
            {
                PropertyName = propertyName;
                Label = label;
                Tooltip = tooltip;
            }
        }

        private readonly struct SectionDescription
        {
            public readonly string Title;
            public readonly PropertyDescription[] Properties;

            public SectionDescription(string title, params PropertyDescription[] properties)
            {
                Title = title;
                Properties = properties;
            }
        }

        private const string BackfaceTextureToggleName = "_PageCurlUseBackfaceTexture";
        private const string BackfaceTextureName = "_PageCurlBackfaceTexture";
        private const string SectionExpandedKeyPrefix = "DoodleStickers.ShaderGUI.";

        private static readonly SectionDescription[] Sections =
        {
            new SectionDescription("Sticker",
                new PropertyDescription("_Color", "Tint", "Multiplies the sticker art. Does not tint the backface or the shadow."),
                new PropertyDescription("_StickerOpacity", "Opacity", "Overall opacity of the sticker, including its shadow.")),
            new SectionDescription("Doodle Wobble",
                new PropertyDescription("_DoodleWobbleTiling", "Tiling", "How many wobble ripples fit across the sticker."),
                new PropertyDescription("_DoodleWobbleStrength", "Strength", "How far the art is pushed by the wobble, as a fraction of the sticker. 0 turns the wobble off."),
                new PropertyDescription("_DoodleWobbleFramesPerSecond", "Frames Per Second", "How often the wobble jumps to a new hand-drawn frame. 0 freezes it."),
                new PropertyDescription("_DoodleWobbleUseUnscaledTime", "Use Unscaled Time", "Keep wobbling while the game is paused (Time.timeScale = 0).")),
            new SectionDescription("Curl",
                new PropertyDescription("_PageCurlRadius", "Radius", "Largest radius of the rolled paper, as a fraction of the sticker's longest side."),
                new PropertyDescription("_PageCurlShading", "Shading", "How much the curved paper darkens as it turns away from the viewer."),
                new PropertyDescription("_PageCurlHighlightStrength", "Highlight", "Brightness of the thin highlight along the roll of the curl.")),
            new SectionDescription("Shadow",
                new PropertyDescription("_PageCurlShadowColor", "Color", "Shadow color. Alpha controls shadow strength."),
                new PropertyDescription("_PageCurlShadowSoftness", "Softness", "Blur of the shadow edge, as a fraction of the sticker's longest side."),
                new PropertyDescription("_PageCurlShadowOffset", "Offset (XY)", "Direction and distance the lifted flap's shadow falls, as a fraction of the sticker's longest side. Z and W are unused."),
                new PropertyDescription("_PageCurlDropShadow", "Drop Shadow", "Cast a soft shadow of the whole lifted flap. Turning it off keeps only the contact shadow at the base of the curl and saves two texture samples.")),
            new SectionDescription("Backface",
                new PropertyDescription("_PageCurlBackfaceColor", "Color", "Color of the back of the sticker paper."),
                new PropertyDescription(BackfaceTextureToggleName, "Use Texture", "Multiply the backface by a paper or glue texture."),
                new PropertyDescription(BackfaceTextureName, "Texture", "Paper or glue texture for the back of the sticker. Set its Wrap Mode to Repeat when tiling it."),
                new PropertyDescription("_PageCurlArtBleed", "Art Bleed", "How much of the printed art shows through the back of the paper, mirrored.")),
            new SectionDescription("Rendering",
                new PropertyDescription("PixelSnap", "Pixel Snap", "Snap vertices to screen pixels for crisp pixel art."),
                new PropertyDescription("_UseUIAlphaClip", "Use Alpha Clip", "Discard fully transparent pixels. Needed only for some UI masking setups."))
        };

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material material = materialEditor.target as Material;
            bool usesBackfaceTexture = material != null && material.IsKeywordEnabled("_DOODLE_STICKER_BACKFACE_TEXTURE");

            for (int sectionIndex = 0; sectionIndex < Sections.Length; sectionIndex++)
            {
                SectionDescription section = Sections[sectionIndex];
                if (!HasAnyProperty(section, properties))
                {
                    continue;
                }

                string expandedKey = SectionExpandedKeyPrefix + section.Title;
                bool isExpanded = SessionState.GetBool(expandedKey, true);
                bool shouldExpand = EditorGUILayout.BeginFoldoutHeaderGroup(isExpanded, section.Title);
                if (shouldExpand != isExpanded)
                {
                    SessionState.SetBool(expandedKey, shouldExpand);
                }

                if (shouldExpand)
                {
                    EditorGUI.indentLevel++;
                    for (int propertyIndex = 0; propertyIndex < section.Properties.Length; propertyIndex++)
                    {
                        PropertyDescription description = section.Properties[propertyIndex];
                        if (description.PropertyName == BackfaceTextureName && !usesBackfaceTexture)
                        {
                            continue;
                        }

                        MaterialProperty property = FindProperty(description.PropertyName, properties, false);
                        if (property != null)
                        {
                            materialEditor.ShaderProperty(property, new GUIContent(description.Label, description.Tooltip));
                        }
                    }

                    if (section.Title == "Backface" && usesBackfaceTexture)
                    {
                        DrawBackfaceWrapModeHint(FindProperty(BackfaceTextureName, properties, false));
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            EditorGUILayout.Space();
            materialEditor.RenderQueueField();
            EditorGUILayout.HelpBox(
                "Sprite stickers render with per-texture copies of this material. Edits made here in the editor apply immediately; " +
                "after changing this material from a script at runtime, call DoodleStickerMaterialCache.RefreshFromTemplates().",
                MessageType.None);
        }

        private static bool HasAnyProperty(SectionDescription section, MaterialProperty[] properties)
        {
            for (int propertyIndex = 0; propertyIndex < section.Properties.Length; propertyIndex++)
            {
                if (FindProperty(section.Properties[propertyIndex].PropertyName, properties, false) != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static void DrawBackfaceWrapModeHint(MaterialProperty backfaceTextureProperty)
        {
            if (backfaceTextureProperty == null || backfaceTextureProperty.textureValue == null)
            {
                return;
            }

            Vector4 scaleAndOffset = backfaceTextureProperty.textureScaleAndOffset;
            bool isTiled = !Mathf.Approximately(scaleAndOffset.x, 1f) || !Mathf.Approximately(scaleAndOffset.y, 1f);
            if (isTiled && backfaceTextureProperty.textureValue.wrapMode != TextureWrapMode.Repeat)
            {
                EditorGUILayout.HelpBox("The backface texture is tiled but its Wrap Mode is not Repeat, so its edges will stretch.", MessageType.Warning);
            }
        }
    }
}
