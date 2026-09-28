using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DoodleStickers.EditorTooling
{
    public sealed class DoodleStickerSpriteResizerWindow : EditorWindow
    {
        private const string SettingsPreferenceKey = "DoodleStickers.SpriteResizer.Settings";
        private const float PreviewThumbnailSize = 40f;

        private static readonly GUIContent SizeModeLabel = new GUIContent("Size Mode", "Power Of Two: pick a standard square size.\nCustom: type any width and height.");
        private static readonly GUIContent TargetSizeLabel = new GUIContent("Target Size", "Power-of-two size of the new texture. The sprite is scaled to fit inside it, keeping its aspect ratio.");
        private static readonly GUIContent CustomWidthLabel = new GUIContent("Width", "Maximum width of the new texture in pixels. The sprite is scaled to fit inside Width x Height, keeping its aspect ratio.");
        private static readonly GUIContent CustomHeightLabel = new GUIContent("Height", "Maximum height of the new texture in pixels. The sprite is scaled to fit inside Width x Height, keeping its aspect ratio.");
        private static readonly GUIContent RoundToMultipleOfFourLabel = new GUIContent("Multiple Of 4", "Keep both sides a multiple of 4 pixels. Required for DXT/BC and Crunch compression; ASTC and ETC2 work with any size.");
        private static readonly GUIContent CanvasShapeLabel = new GUIContent("Canvas Shape", "Square: always Target Size x Target Size.\nPower Of Two Per Axis: each side is the smallest power of two that fits the scaled sprite, up to Target Size. Saves memory for wide or tall sprites.");
        private static readonly GUIContent CustomCanvasShapeLabel = new GUIContent("Canvas Shape", "Full Target Size: always exactly Width x Height, with the sprite centred.\nFit To Art: the canvas hugs the scaled sprite plus padding, never larger than Width x Height.");
        private static readonly GUIContent[] PowerOfTwoCanvasShapeOptions = { new GUIContent("Square"), new GUIContent("Power Of Two Per Axis") };
        private static readonly GUIContent[] CustomCanvasShapeOptions = { new GUIContent("Full Target Size"), new GUIContent("Fit To Art") };
        private static readonly GUIContent[] SizeModeOptions = { new GUIContent("Power Of Two"), new GUIContent("Custom") };
        private static readonly int[] PowerOfTwoCanvasShapeValues = { (int)DoodleStickerCanvasShape.FullTargetSize, (int)DoodleStickerCanvasShape.PowerOfTwoPerAxis };
        private static readonly int[] CustomCanvasShapeValues = { (int)DoodleStickerCanvasShape.FullTargetSize, (int)DoodleStickerCanvasShape.FitToArt };
        private static readonly GUIContent PaddingLabel = new GUIContent("Padding", "Transparent pixels kept around the sprite on every side, so filtering and the curl edge never touch the texture border.");
        private static readonly GUIContent TrimLabel = new GUIContent("Trim Transparent Borders", "Crop empty space around the art before scaling, so more of the new texture is used for the art itself.");
        private static readonly GUIContent UpscaleLabel = new GUIContent("Allow Upscaling", "Enlarge sprites smaller than the target. Off keeps small sprites at their original resolution.");
        private static readonly GUIContent KeepWorldSizeLabel = new GUIContent("Keep World Size", "Adjust Pixels Per Unit and the pivot so the new sprite appears at exactly the same size and position as the original.");
        private static readonly GUIContent EdgeBleedLabel = new GUIContent("Edge Color Bleed", "Pixels of edge color spread into transparent areas. Prevents dark fringes when the texture is filtered or compressed.");
        private static readonly GUIContent MobileCompressionLabel = new GUIContent("Mobile Compression", "ASTC format for Android and iOS. Keep Source copies the original's settings. Smaller textures pack more detail into each block, so a finer format (4x4 or 6x6) keeps them crisper; each step up roughly doubles memory.");
        private static readonly GUIContent OutputFolderLabel = new GUIContent("Output Folder", "Folder for the new textures. Leave empty to save next to each source texture.");
        private static readonly GUIContent SuffixLabel = new GUIContent("File Name Suffix", "Appended to the sprite name. {size} becomes the canvas size, for example 512 or 512x256.");
        private static readonly GUIContent OverwriteLabel = new GUIContent("Overwrite Existing", "Replace files with the same name instead of creating numbered copies.");

        private readonly List<Sprite> sprites = new List<Sprite>();
        private readonly Dictionary<Sprite, DoodleStickerSpriteSourceImage> sourceSummaries = new Dictionary<Sprite, DoodleStickerSpriteSourceImage>();
        private readonly Dictionary<Sprite, string> sourceProblems = new Dictionary<Sprite, string>();
        private readonly List<DoodleStickerSpriteResizeResult> lastResults = new List<DoodleStickerSpriteResizeResult>();
        private DoodleStickerSpriteResizeSettings settings;
        private Vector2 scrollPosition;

        [MenuItem("Tools/Doodle Stickers/Sprite Resizer")]
        public static DoodleStickerSpriteResizerWindow Open()
        {
            DoodleStickerSpriteResizerWindow window = GetWindow<DoodleStickerSpriteResizerWindow>();
            window.titleContent = new GUIContent("Sprite Resizer");
            window.minSize = new Vector2(420f, 480f);
            window.Show();
            return window;
        }

        [MenuItem("Assets/Doodle Stickers/Resize Sprites", false, 1100)]
        private static void OpenWithSelection()
        {
            Open().AddSprites(GetSelectedSprites());
        }

        [MenuItem("Assets/Doodle Stickers/Resize Sprites", true)]
        private static bool CanOpenWithSelection()
        {
            return GetSelectedSprites().Count > 0;
        }

        private void OnEnable()
        {
            settings = LoadSettings();
        }

        private void OnDisable()
        {
            SaveSettings();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawSpriteList();
            EditorGUILayout.Space(8f);
            DrawSettings();
            EditorGUILayout.Space(8f);
            DrawResizeButton();
            DrawResults();

            EditorGUILayout.EndScrollView();
        }

        public void AddSprites(IEnumerable<Sprite> spritesToAdd)
        {
            foreach (Sprite sprite in spritesToAdd)
            {
                if (sprite != null && !sprites.Contains(sprite))
                {
                    sprites.Add(sprite);
                }
            }
            Repaint();
        }

        private void DrawSpriteList()
        {
            EditorGUILayout.LabelField("Sprites", EditorStyles.boldLabel);

            Rect dropArea = GUILayoutUtility.GetRect(0f, 44f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag sprites or textures here", EditorStyles.helpBox);
            HandleDragAndDrop(dropArea);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Selected"))
                {
                    AddSprites(GetSelectedSprites());
                }
                using (new EditorGUI.DisabledScope(sprites.Count == 0))
                {
                    if (GUILayout.Button("Clear"))
                    {
                        sprites.Clear();
                        lastResults.Clear();
                    }
                }
            }

            int spriteToRemove = -1;
            for (int spriteIndex = 0; spriteIndex < sprites.Count; spriteIndex++)
            {
                Sprite sprite = sprites[spriteIndex];
                if (sprite == null)
                {
                    spriteToRemove = spriteIndex;
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    Rect thumbnailRect = GUILayoutUtility.GetRect(PreviewThumbnailSize, PreviewThumbnailSize, GUILayout.Width(PreviewThumbnailSize));
                    Texture2D thumbnail = AssetPreview.GetAssetPreview(sprite);
                    if (thumbnail != null)
                    {
                        GUI.DrawTexture(thumbnailRect, thumbnail, ScaleMode.ScaleToFit);
                    }

                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(sprite.name, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(DescribePlan(sprite), EditorStyles.miniLabel);
                    }

                    if (GUILayout.Button("Remove", GUILayout.Width(64f)))
                    {
                        spriteToRemove = spriteIndex;
                    }
                }
            }

            if (spriteToRemove >= 0)
            {
                sprites.RemoveAt(spriteToRemove);
            }
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            settings.targetSizeMode = (DoodleStickerTargetSizeMode)EditorGUILayout.Popup(SizeModeLabel, (int)settings.targetSizeMode, SizeModeOptions);
            if (settings.targetSizeMode == DoodleStickerTargetSizeMode.Custom)
            {
                DrawCustomTargetSize();
            }
            else
            {
                DrawPowerOfTwoTargetSize();
            }

            settings.padding = EditorGUILayout.IntSlider(PaddingLabel, settings.padding, 0, 32);
            settings.trimTransparentBorders = EditorGUILayout.Toggle(TrimLabel, settings.trimTransparentBorders);
            settings.allowUpscaling = EditorGUILayout.Toggle(UpscaleLabel, settings.allowUpscaling);
            settings.keepWorldSize = EditorGUILayout.Toggle(KeepWorldSizeLabel, settings.keepWorldSize);
            settings.edgeBleedPixels = EditorGUILayout.IntSlider(EdgeBleedLabel, settings.edgeBleedPixels, 0, 32);
            settings.mobileCompression = (DoodleStickerMobileCompression)EditorGUILayout.EnumPopup(MobileCompressionLabel, settings.mobileCompression);

            using (new EditorGUILayout.HorizontalScope())
            {
                settings.outputFolder = EditorGUILayout.TextField(OutputFolderLabel, settings.outputFolder);
                if (GUILayout.Button("…", GUILayout.Width(28f)))
                {
                    string chosenFolder = EditorUtility.OpenFolderPanel("Output Folder", "Assets", string.Empty);
                    string projectRelativeFolder = ToProjectRelativePath(chosenFolder);
                    if (projectRelativeFolder != null)
                    {
                        settings.outputFolder = projectRelativeFolder;
                        GUI.FocusControl(null);
                    }
                }
            }
            settings.fileNameSuffix = EditorGUILayout.TextField(SuffixLabel, settings.fileNameSuffix);
            settings.overwriteExisting = EditorGUILayout.Toggle(OverwriteLabel, settings.overwriteExisting);

            if (EditorGUI.EndChangeCheck())
            {
                SaveSettings();
            }

            if (!string.IsNullOrWhiteSpace(settings.outputFolder) && !settings.outputFolder.StartsWith("Assets"))
            {
                EditorGUILayout.HelpBox("The output folder must be inside Assets.", MessageType.Error);
            }
            EditorGUILayout.HelpBox("Originals are never changed. If a Sprite Atlas packs the output folder, both the originals and the resized copies are packed; keep them in separate folders.", MessageType.None);
        }

        private void DrawPowerOfTwoTargetSize()
        {
            string[] sizeLabels = new string[DoodleStickerSpriteResizer.PowerOfTwoSizes.Length];
            int selectedSizeIndex = 0;
            for (int sizeIndex = 0; sizeIndex < sizeLabels.Length; sizeIndex++)
            {
                sizeLabels[sizeIndex] = DoodleStickerSpriteResizer.PowerOfTwoSizes[sizeIndex].ToString();
                if (DoodleStickerSpriteResizer.PowerOfTwoSizes[sizeIndex] == settings.targetSize)
                {
                    selectedSizeIndex = sizeIndex;
                }
            }
            selectedSizeIndex = EditorGUILayout.Popup(TargetSizeLabel, selectedSizeIndex, sizeLabels);
            settings.targetSize = DoodleStickerSpriteResizer.PowerOfTwoSizes[selectedSizeIndex];

            settings.canvasShape = DrawCanvasShapePopup(CanvasShapeLabel, PowerOfTwoCanvasShapeOptions, PowerOfTwoCanvasShapeValues);
        }

        private void DrawCustomTargetSize()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Target Size", CustomWidthLabel.tooltip));
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 44f;
                settings.customTargetWidth = Mathf.Clamp(EditorGUILayout.DelayedIntField(CustomWidthLabel, settings.customTargetWidth), DoodleStickerSpriteResizer.MinimumCustomSize, DoodleStickerSpriteResizer.MaximumCustomSize);
                settings.customTargetHeight = Mathf.Clamp(EditorGUILayout.DelayedIntField(CustomHeightLabel, settings.customTargetHeight), DoodleStickerSpriteResizer.MinimumCustomSize, DoodleStickerSpriteResizer.MaximumCustomSize);
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                if (GUILayout.Button("Square", EditorStyles.miniButtonLeft))
                {
                    int longestSide = Mathf.Max(settings.customTargetWidth, settings.customTargetHeight);
                    settings.customTargetWidth = longestSide;
                    settings.customTargetHeight = longestSide;
                    GUI.FocusControl(null);
                }
                using (new EditorGUI.DisabledScope(!TryGetFirstSpriteAspect(out _)))
                {
                    if (GUILayout.Button(new GUIContent("Match Sprite Aspect", "Keep the longer side and set the other from the first sprite's art, so no canvas space is wasted."), EditorStyles.miniButtonRight))
                    {
                        MatchFirstSpriteAspect();
                        GUI.FocusControl(null);
                    }
                }
            }

            settings.roundToMultipleOfFour = EditorGUILayout.Toggle(RoundToMultipleOfFourLabel, settings.roundToMultipleOfFour);
            settings.canvasShape = DrawCanvasShapePopup(CustomCanvasShapeLabel, CustomCanvasShapeOptions, CustomCanvasShapeValues);

            Vector2Int effectiveSize = DoodleStickerSpriteResizer.GetEffectiveTargetSize(settings);
            if (effectiveSize.x != settings.customTargetWidth || effectiveSize.y != settings.customTargetHeight)
            {
                EditorGUILayout.HelpBox("Rounded down to " + effectiveSize.x + "×" + effectiveSize.y + " to keep both sides a multiple of 4.", MessageType.Info);
            }
        }

        private DoodleStickerCanvasShape DrawCanvasShapePopup(GUIContent label, GUIContent[] options, int[] values)
        {
            int currentValue = (int)DoodleStickerSpriteResizer.GetEffectiveCanvasShape(settings);
            return (DoodleStickerCanvasShape)EditorGUILayout.IntPopup(label, currentValue, options, values);
        }

        private bool TryGetFirstSpriteAspect(out float aspect)
        {
            aspect = 1f;
            for (int spriteIndex = 0; spriteIndex < sprites.Count; spriteIndex++)
            {
                Sprite sprite = sprites[spriteIndex];
                if (sprite == null)
                {
                    continue;
                }
                if (!sourceSummaries.TryGetValue(sprite, out DoodleStickerSpriteSourceImage summary))
                {
                    DescribePlan(sprite);
                    if (!sourceSummaries.TryGetValue(sprite, out summary))
                    {
                        continue;
                    }
                }

                RectInt artRect = settings.trimTransparentBorders && summary.OpaqueBounds.width > 0
                    ? summary.OpaqueBounds
                    : new RectInt(0, 0, summary.Width, summary.Height);
                aspect = (float)artRect.width / artRect.height;
                return true;
            }
            return false;
        }

        private void MatchFirstSpriteAspect()
        {
            if (!TryGetFirstSpriteAspect(out float aspect))
            {
                return;
            }

            int padding = settings.padding * 2;
            int longestSide = Mathf.Max(settings.customTargetWidth, settings.customTargetHeight);
            int artLongestSide = Mathf.Max(longestSide - padding, 1);
            if (aspect >= 1f)
            {
                settings.customTargetWidth = longestSide;
                settings.customTargetHeight = Mathf.Clamp(Mathf.RoundToInt(artLongestSide / aspect) + padding, DoodleStickerSpriteResizer.MinimumCustomSize, DoodleStickerSpriteResizer.MaximumCustomSize);
            }
            else
            {
                settings.customTargetHeight = longestSide;
                settings.customTargetWidth = Mathf.Clamp(Mathf.RoundToInt(artLongestSide * aspect) + padding, DoodleStickerSpriteResizer.MinimumCustomSize, DoodleStickerSpriteResizer.MaximumCustomSize);
            }
            SaveSettings();
        }

        private void DrawResizeButton()
        {
            bool hasValidFolder = string.IsNullOrWhiteSpace(settings.outputFolder) || settings.outputFolder.StartsWith("Assets");
            using (new EditorGUI.DisabledScope(sprites.Count == 0 || !hasValidFolder))
            {
                if (GUILayout.Button(sprites.Count == 1 ? "Resize 1 Sprite" : "Resize " + sprites.Count + " Sprites", GUILayout.Height(30f)))
                {
                    ResizeAll();
                }
            }
        }

        private void DrawResults()
        {
            if (lastResults.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
            List<Object> createdAssets = new List<Object>();

            for (int resultIndex = 0; resultIndex < lastResults.Count; resultIndex++)
            {
                DoodleStickerSpriteResizeResult result = lastResults[resultIndex];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField((result.Succeeded ? "✔ " : "✖ ") + (result.OutputPath ?? string.Empty) + "\n" + result.Message, EditorStyles.wordWrappedMiniLabel);
                    if (result.Succeeded)
                    {
                        Object createdAsset = AssetDatabase.LoadAssetAtPath<Object>(result.OutputPath);
                        if (createdAsset != null)
                        {
                            createdAssets.Add(createdAsset);
                            if (GUILayout.Button("Ping", GUILayout.Width(48f)))
                            {
                                EditorGUIUtility.PingObject(createdAsset);
                            }
                        }
                    }
                }
            }

            if (createdAssets.Count > 0 && GUILayout.Button("Select Resized Textures"))
            {
                Selection.objects = createdAssets.ToArray();
            }
        }

        private void ResizeAll()
        {
            lastResults.Clear();
            try
            {
                for (int spriteIndex = 0; spriteIndex < sprites.Count; spriteIndex++)
                {
                    Sprite sprite = sprites[spriteIndex];
                    if (EditorUtility.DisplayCancelableProgressBar("Resizing Sprites", sprite.name, (float)spriteIndex / sprites.Count))
                    {
                        break;
                    }
                    lastResults.Add(DoodleStickerSpriteResizer.Resize(sprite, settings));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
        }

        private string DescribePlan(Sprite sprite)
        {
            if (!sourceSummaries.TryGetValue(sprite, out DoodleStickerSpriteSourceImage summary))
            {
                if (sourceProblems.TryGetValue(sprite, out string cachedProblem))
                {
                    return cachedProblem;
                }

                if (!DoodleStickerSpriteResizer.TryLoadSource(sprite, out summary, out string problem))
                {
                    sourceProblems[sprite] = problem;
                    return problem;
                }
                summary.Pixels = null;
                sourceSummaries[sprite] = summary;
            }

            DoodleStickerSpriteResizePlan plan = DoodleStickerSpriteResizer.CreatePlan(summary, settings);
            if (!plan.IsValid)
            {
                return plan.Problem;
            }

            return summary.Width + "×" + summary.Height
                + "  →  " + plan.CanvasSize.x + "×" + plan.CanvasSize.y
                + "   (art " + plan.ContentOutputSize.x + "×" + plan.ContentOutputSize.y
                + ", " + Mathf.RoundToInt(plan.Scale.x * 100f) + "%)";
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            Event currentEvent = Event.current;
            if (!dropArea.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform)
            {
                List<Sprite> draggedSprites = CollectSprites(DragAndDrop.objectReferences);
                DragAndDrop.visualMode = draggedSprites.Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                if (currentEvent.type == EventType.DragPerform && draggedSprites.Count > 0)
                {
                    DragAndDrop.AcceptDrag();
                    AddSprites(draggedSprites);
                }
                currentEvent.Use();
            }
        }

        private static List<Sprite> GetSelectedSprites()
        {
            return CollectSprites(Selection.objects);
        }

        private static List<Sprite> CollectSprites(Object[] objects)
        {
            List<Sprite> collectedSprites = new List<Sprite>();
            foreach (Object selectedObject in objects)
            {
                if (selectedObject is Sprite selectedSprite)
                {
                    collectedSprites.Add(selectedSprite);
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(selectedObject);
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                foreach (Object subAsset in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                {
                    if (subAsset is Sprite subSprite)
                    {
                        collectedSprites.Add(subSprite);
                    }
                }
            }
            return collectedSprites;
        }

        private static string ToProjectRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return null;
            }

            string normalizedPath = absolutePath.Replace('\\', '/');
            string assetsPath = Application.dataPath.Replace('\\', '/');
            if (normalizedPath == assetsPath)
            {
                return "Assets";
            }
            return normalizedPath.StartsWith(assetsPath + "/") ? "Assets" + normalizedPath.Substring(assetsPath.Length) : null;
        }

        private static DoodleStickerSpriteResizeSettings LoadSettings()
        {
            string json = EditorPrefs.GetString(SettingsPreferenceKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new DoodleStickerSpriteResizeSettings();
            }
            return JsonUtility.FromJson<DoodleStickerSpriteResizeSettings>(json) ?? new DoodleStickerSpriteResizeSettings();
        }

        private void SaveSettings()
        {
            if (settings != null)
            {
                EditorPrefs.SetString(SettingsPreferenceKey, JsonUtility.ToJson(settings));
            }
        }
    }
}
