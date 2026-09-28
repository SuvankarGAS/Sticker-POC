using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DoodleStickers.EditorTooling
{
    public enum DoodleStickerTargetSizeMode
    {
        PowerOfTwo,
        Custom
    }

    public enum DoodleStickerCanvasShape
    {
        FullTargetSize,
        PowerOfTwoPerAxis,
        FitToArt
    }

    public enum DoodleStickerMobileCompression
    {
        KeepSource,
        Astc4x4,
        Astc5x5,
        Astc6x6,
        Astc8x8
    }

    [Serializable]
    public sealed class DoodleStickerSpriteResizeSettings
    {
        public DoodleStickerTargetSizeMode targetSizeMode = DoodleStickerTargetSizeMode.PowerOfTwo;
        public int targetSize = 512;
        public int customTargetWidth = 512;
        public int customTargetHeight = 512;
        public bool roundToMultipleOfFour = true;
        public DoodleStickerCanvasShape canvasShape = DoodleStickerCanvasShape.FullTargetSize;
        public int padding = 4;
        public bool trimTransparentBorders = true;
        public bool allowUpscaling;
        public bool keepWorldSize = true;
        public int edgeBleedPixels = 8;
        public DoodleStickerMobileCompression mobileCompression = DoodleStickerMobileCompression.KeepSource;
        public string outputFolder = "Assets/ResizedSprites";
        public string fileNameSuffix = "_{size}";
        public bool overwriteExisting;
    }

    public sealed class DoodleStickerSpriteSourceImage
    {
        public Sprite Sprite;
        public string AssetPath;
        public TextureImporter Importer;
        public int Width;
        public int Height;
        public Color32[] Pixels;
        public Vector2 PivotInPixels;
        public float PixelsPerUnit;
        public RectInt OpaqueBounds;
        public bool UsedFullResolutionSource;
    }

    public readonly struct DoodleStickerSpriteResizePlan
    {
        public readonly bool IsValid;
        public readonly string Problem;
        public readonly RectInt ContentRect;
        public readonly Vector2Int ContentOutputSize;
        public readonly Vector2Int CanvasSize;
        public readonly Vector2Int ContentOffset;
        public readonly Vector2 Scale;

        public DoodleStickerSpriteResizePlan(RectInt contentRect, Vector2Int contentOutputSize, Vector2Int canvasSize, Vector2Int contentOffset, Vector2 scale)
        {
            IsValid = true;
            Problem = null;
            ContentRect = contentRect;
            ContentOutputSize = contentOutputSize;
            CanvasSize = canvasSize;
            ContentOffset = contentOffset;
            Scale = scale;
        }

        private DoodleStickerSpriteResizePlan(string problem)
        {
            IsValid = false;
            Problem = problem;
            ContentRect = default;
            ContentOutputSize = default;
            CanvasSize = default;
            ContentOffset = default;
            Scale = Vector2.one;
        }

        public static DoodleStickerSpriteResizePlan Invalid(string problem) => new DoodleStickerSpriteResizePlan(problem);
    }

    public readonly struct DoodleStickerSpriteResizeResult
    {
        public readonly bool Succeeded;
        public readonly string Message;
        public readonly string OutputPath;

        public DoodleStickerSpriteResizeResult(bool succeeded, string message, string outputPath)
        {
            Succeeded = succeeded;
            Message = message;
            OutputPath = outputPath;
        }
    }

    public static class DoodleStickerSpriteResizer
    {
        public static readonly int[] PowerOfTwoSizes = { 32, 64, 128, 256, 512, 1024, 2048, 4096 };
        public const int MinimumCustomSize = 4;
        public const int MaximumCustomSize = 8192;

        private const byte OpaqueAlphaThreshold = 2;
        private static readonly string[] PlatformNames = { "Standalone", "Android", "iPhone", "WebGL", "Server" };
        private static readonly string[] MobilePlatformNames = { "Android", "iPhone" };

        public static bool TryLoadSource(Sprite sprite, out DoodleStickerSpriteSourceImage sourceImage, out string problem)
        {
            sourceImage = null;
            problem = null;

            if (sprite == null)
            {
                problem = "No sprite.";
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(sprite);
            if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter importer))
            {
                problem = "The sprite does not come from an imported image file.";
                return false;
            }

            Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (importedTexture == null)
            {
                problem = "The sprite's source texture could not be loaded.";
                return false;
            }
            Color32[] fullPixels = null;
            int fullWidth = 0;
            int fullHeight = 0;
            bool usedFullResolutionSource = false;

            string extension = Path.GetExtension(assetPath).ToLowerInvariant();
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
            {
                Texture2D decodedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (decodedTexture.LoadImage(File.ReadAllBytes(assetPath), false))
                    {
                        fullPixels = decodedTexture.GetPixels32();
                        fullWidth = decodedTexture.width;
                        fullHeight = decodedTexture.height;
                        usedFullResolutionSource = true;
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(decodedTexture);
                }
            }

            if (fullPixels == null)
            {
                fullPixels = ReadImportedTexturePixels(importedTexture);
                fullWidth = importedTexture.width;
                fullHeight = importedTexture.height;
            }

            Vector2 importedToSource = new Vector2((float)fullWidth / importedTexture.width, (float)fullHeight / importedTexture.height);
            Rect spriteRect = sprite.rect;
            RectInt sourceRect = new RectInt(
                Mathf.RoundToInt(spriteRect.x * importedToSource.x),
                Mathf.RoundToInt(spriteRect.y * importedToSource.y),
                Mathf.RoundToInt(spriteRect.width * importedToSource.x),
                Mathf.RoundToInt(spriteRect.height * importedToSource.y));
            sourceRect.width = Mathf.Clamp(sourceRect.width, 1, fullWidth - sourceRect.x);
            sourceRect.height = Mathf.Clamp(sourceRect.height, 1, fullHeight - sourceRect.y);

            Color32[] spritePixels = new Color32[sourceRect.width * sourceRect.height];
            for (int rowIndex = 0; rowIndex < sourceRect.height; rowIndex++)
            {
                Array.Copy(fullPixels, (sourceRect.y + rowIndex) * fullWidth + sourceRect.x, spritePixels, rowIndex * sourceRect.width, sourceRect.width);
            }

            float spritePixelsToSource = usedFullResolutionSource ? importedToSource.x : 1f;
            sourceImage = new DoodleStickerSpriteSourceImage
            {
                Sprite = sprite,
                AssetPath = assetPath,
                Importer = importer,
                Width = sourceRect.width,
                Height = sourceRect.height,
                Pixels = spritePixels,
                PivotInPixels = Vector2.Scale(sprite.pivot, importedToSource),
                PixelsPerUnit = sprite.pixelsPerUnit * spritePixelsToSource,
                OpaqueBounds = FindOpaqueBounds(spritePixels, sourceRect.width, sourceRect.height),
                UsedFullResolutionSource = usedFullResolutionSource
            };
            return true;
        }

        public static DoodleStickerSpriteResizePlan CreatePlan(DoodleStickerSpriteSourceImage sourceImage, DoodleStickerSpriteResizeSettings settings)
        {
            RectInt contentRect = settings.trimTransparentBorders
                ? sourceImage.OpaqueBounds
                : new RectInt(0, 0, sourceImage.Width, sourceImage.Height);
            if (contentRect.width <= 0 || contentRect.height <= 0)
            {
                return DoodleStickerSpriteResizePlan.Invalid("The sprite is fully transparent.");
            }

            Vector2Int targetSize = GetEffectiveTargetSize(settings);
            int padding = Mathf.Clamp(settings.padding, 0, Mathf.Min(targetSize.x, targetSize.y) / 4);
            Vector2Int availableSize = new Vector2Int(targetSize.x - padding * 2, targetSize.y - padding * 2);
            float uniformScale = Mathf.Min((float)availableSize.x / contentRect.width, (float)availableSize.y / contentRect.height);
            if (!settings.allowUpscaling)
            {
                uniformScale = Mathf.Min(uniformScale, 1f);
            }

            Vector2Int contentOutputSize = new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt(contentRect.width * uniformScale), 1, availableSize.x),
                Mathf.Clamp(Mathf.RoundToInt(contentRect.height * uniformScale), 1, availableSize.y));

            Vector2Int canvasSize = ResolveCanvasSize(settings, targetSize, contentOutputSize, padding);

            Vector2Int contentOffset = new Vector2Int(
                (canvasSize.x - contentOutputSize.x) / 2,
                (canvasSize.y - contentOutputSize.y) / 2);

            Vector2 scale = new Vector2((float)contentOutputSize.x / contentRect.width, (float)contentOutputSize.y / contentRect.height);
            return new DoodleStickerSpriteResizePlan(contentRect, contentOutputSize, canvasSize, contentOffset, scale);
        }

        public static Vector2Int GetEffectiveTargetSize(DoodleStickerSpriteResizeSettings settings)
        {
            if (settings.targetSizeMode == DoodleStickerTargetSizeMode.PowerOfTwo)
            {
                int powerOfTwoSize = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(settings.targetSize, 1)), PowerOfTwoSizes[0], PowerOfTwoSizes[PowerOfTwoSizes.Length - 1]);
                return new Vector2Int(powerOfTwoSize, powerOfTwoSize);
            }

            Vector2Int customSize = new Vector2Int(
                Mathf.Clamp(settings.customTargetWidth, MinimumCustomSize, MaximumCustomSize),
                Mathf.Clamp(settings.customTargetHeight, MinimumCustomSize, MaximumCustomSize));
            return settings.roundToMultipleOfFour
                ? new Vector2Int(customSize.x / 4 * 4, customSize.y / 4 * 4)
                : customSize;
        }

        public static DoodleStickerCanvasShape GetEffectiveCanvasShape(DoodleStickerSpriteResizeSettings settings)
        {
            bool isCustomSize = settings.targetSizeMode == DoodleStickerTargetSizeMode.Custom;
            if (isCustomSize && settings.canvasShape == DoodleStickerCanvasShape.PowerOfTwoPerAxis)
            {
                return DoodleStickerCanvasShape.FitToArt;
            }
            if (!isCustomSize && settings.canvasShape == DoodleStickerCanvasShape.FitToArt)
            {
                return DoodleStickerCanvasShape.PowerOfTwoPerAxis;
            }
            return settings.canvasShape;
        }

        private static Vector2Int ResolveCanvasSize(DoodleStickerSpriteResizeSettings settings, Vector2Int targetSize, Vector2Int contentOutputSize, int padding)
        {
            Vector2Int paddedContentSize = new Vector2Int(contentOutputSize.x + padding * 2, contentOutputSize.y + padding * 2);
            switch (GetEffectiveCanvasShape(settings))
            {
                case DoodleStickerCanvasShape.PowerOfTwoPerAxis:
                    return new Vector2Int(
                        Mathf.Min(Mathf.NextPowerOfTwo(paddedContentSize.x), targetSize.x),
                        Mathf.Min(Mathf.NextPowerOfTwo(paddedContentSize.y), targetSize.y));
                case DoodleStickerCanvasShape.FitToArt:
                    return settings.roundToMultipleOfFour
                        ? new Vector2Int(
                            Mathf.Min(RoundUpToMultipleOfFour(paddedContentSize.x), targetSize.x),
                            Mathf.Min(RoundUpToMultipleOfFour(paddedContentSize.y), targetSize.y))
                        : paddedContentSize;
                default:
                    return targetSize;
            }
        }

        private static int RoundUpToMultipleOfFour(int value)
        {
            return (value + 3) / 4 * 4;
        }

        public static string GetOutputPath(DoodleStickerSpriteSourceImage sourceImage, DoodleStickerSpriteResizePlan plan, DoodleStickerSpriteResizeSettings settings)
        {
            string folder = string.IsNullOrWhiteSpace(settings.outputFolder)
                ? Path.GetDirectoryName(sourceImage.AssetPath).Replace('\\', '/')
                : settings.outputFolder.TrimEnd('/');

            string sizeLabel = plan.CanvasSize.x == plan.CanvasSize.y
                ? plan.CanvasSize.x.ToString()
                : plan.CanvasSize.x + "x" + plan.CanvasSize.y;
            string suffix = (settings.fileNameSuffix ?? string.Empty).Replace("{size}", sizeLabel);
            string path = folder + "/" + sourceImage.Sprite.name + suffix + ".png";
            return settings.overwriteExisting ? path : AssetDatabase.GenerateUniqueAssetPath(path);
        }

        public static DoodleStickerSpriteResizeResult Resize(Sprite sprite, DoodleStickerSpriteResizeSettings settings)
        {
            if (!TryLoadSource(sprite, out DoodleStickerSpriteSourceImage sourceImage, out string loadProblem))
            {
                return new DoodleStickerSpriteResizeResult(false, loadProblem, null);
            }

            DoodleStickerSpriteResizePlan plan = CreatePlan(sourceImage, settings);
            if (!plan.IsValid)
            {
                return new DoodleStickerSpriteResizeResult(false, plan.Problem, null);
            }

            string outputPath = GetOutputPath(sourceImage, plan, settings);
            if (string.Equals(outputPath, sourceImage.AssetPath, StringComparison.OrdinalIgnoreCase))
            {
                return new DoodleStickerSpriteResizeResult(false, "The output path is the source file itself. Change the output folder or file name suffix.", null);
            }

            Color32[] canvasPixels = RenderCanvas(sourceImage, plan, settings.edgeBleedPixels);
            EnsureFolderExists(Path.GetDirectoryName(outputPath).Replace('\\', '/'));

            Texture2D canvasTexture = new Texture2D(plan.CanvasSize.x, plan.CanvasSize.y, TextureFormat.RGBA32, false);
            try
            {
                canvasTexture.SetPixels32(canvasPixels);
                canvasTexture.Apply(false);
                File.WriteAllBytes(outputPath, canvasTexture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasTexture);
            }

            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            ConfigureOutputImporter(sourceImage, plan, settings, outputPath);

            string message = sourceImage.Width + "x" + sourceImage.Height + " → " + plan.CanvasSize.x + "x" + plan.CanvasSize.y
                + " (content " + plan.ContentOutputSize.x + "x" + plan.ContentOutputSize.y + ", scale " + plan.Scale.x.ToString("0.###") + ")"
                + (sourceImage.UsedFullResolutionSource ? string.Empty : " — read from the imported texture, not the original file");
            return new DoodleStickerSpriteResizeResult(true, message, outputPath);
        }

        private static Color32[] RenderCanvas(DoodleStickerSpriteSourceImage sourceImage, DoodleStickerSpriteResizePlan plan, int edgeBleedPixels)
        {
            RectInt contentRect = plan.ContentRect;
            float[] contentPixels = ExtractPremultipliedLinear(sourceImage.Pixels, sourceImage.Width, contentRect);
            float[] horizontallyResized = ResampleAxis(contentPixels, contentRect.width, contentRect.height, plan.ContentOutputSize.x, true);
            float[] resizedContent = ResampleAxis(horizontallyResized, plan.ContentOutputSize.x, contentRect.height, plan.ContentOutputSize.y, false);

            int canvasWidth = plan.CanvasSize.x;
            int canvasHeight = plan.CanvasSize.y;
            float[] canvas = new float[canvasWidth * canvasHeight * 4];
            for (int rowIndex = 0; rowIndex < plan.ContentOutputSize.y; rowIndex++)
            {
                int canvasRow = rowIndex + plan.ContentOffset.y;
                Array.Copy(resizedContent, rowIndex * plan.ContentOutputSize.x * 4, canvas, (canvasRow * canvasWidth + plan.ContentOffset.x) * 4, plan.ContentOutputSize.x * 4);
            }

            Color32[] canvasPixels = ConvertToStraightSrgb(canvas, canvasWidth * canvasHeight);
            BleedEdgeColors(canvasPixels, canvasWidth, canvasHeight, edgeBleedPixels);
            return canvasPixels;
        }

        private static float[] ExtractPremultipliedLinear(Color32[] pixels, int imageWidth, RectInt contentRect)
        {
            float[] srgbToLinear = BuildSrgbToLinearTable();
            float[] contentPixels = new float[contentRect.width * contentRect.height * 4];
            for (int rowIndex = 0; rowIndex < contentRect.height; rowIndex++)
            {
                int sourceRowStart = (contentRect.y + rowIndex) * imageWidth + contentRect.x;
                for (int columnIndex = 0; columnIndex < contentRect.width; columnIndex++)
                {
                    Color32 pixel = pixels[sourceRowStart + columnIndex];
                    float alpha = pixel.a / 255f;
                    int outputIndex = (rowIndex * contentRect.width + columnIndex) * 4;
                    contentPixels[outputIndex] = srgbToLinear[pixel.r] * alpha;
                    contentPixels[outputIndex + 1] = srgbToLinear[pixel.g] * alpha;
                    contentPixels[outputIndex + 2] = srgbToLinear[pixel.b] * alpha;
                    contentPixels[outputIndex + 3] = alpha;
                }
            }
            return contentPixels;
        }

        private static float[] ResampleAxis(float[] pixels, int width, int height, int outputLength, bool isHorizontal)
        {
            int inputLength = isHorizontal ? width : height;
            int outputWidth = isHorizontal ? outputLength : width;
            int outputHeight = isHorizontal ? height : outputLength;
            float[] output = new float[outputWidth * outputHeight * 4];

            BuildResampleWeights(inputLength, outputLength, out int[] firstSourceIndices, out float[][] sourceWeights);
            int lineCount = isHorizontal ? height : width;

            for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
            {
                for (int outputIndex = 0; outputIndex < outputLength; outputIndex++)
                {
                    float red = 0f, green = 0f, blue = 0f, alpha = 0f;
                    float[] weights = sourceWeights[outputIndex];
                    int firstSourceIndex = firstSourceIndices[outputIndex];

                    for (int weightIndex = 0; weightIndex < weights.Length; weightIndex++)
                    {
                        int sourceIndex = firstSourceIndex + weightIndex;
                        int pixelIndex = isHorizontal ? (lineIndex * width + sourceIndex) * 4 : (sourceIndex * width + lineIndex) * 4;
                        float weight = weights[weightIndex];
                        red += pixels[pixelIndex] * weight;
                        green += pixels[pixelIndex + 1] * weight;
                        blue += pixels[pixelIndex + 2] * weight;
                        alpha += pixels[pixelIndex + 3] * weight;
                    }

                    int destinationIndex = isHorizontal ? (lineIndex * outputWidth + outputIndex) * 4 : (outputIndex * outputWidth + lineIndex) * 4;
                    output[destinationIndex] = red;
                    output[destinationIndex + 1] = green;
                    output[destinationIndex + 2] = blue;
                    output[destinationIndex + 3] = alpha;
                }
            }
            return output;
        }

        private static void BuildResampleWeights(int inputLength, int outputLength, out int[] firstSourceIndices, out float[][] sourceWeights)
        {
            firstSourceIndices = new int[outputLength];
            sourceWeights = new float[outputLength][];
            float inputPerOutput = (float)inputLength / outputLength;

            for (int outputIndex = 0; outputIndex < outputLength; outputIndex++)
            {
                if (inputPerOutput >= 1f)
                {
                    float coverageStart = outputIndex * inputPerOutput;
                    float coverageEnd = coverageStart + inputPerOutput;
                    int firstSourceIndex = Mathf.FloorToInt(coverageStart);
                    int lastSourceIndex = Mathf.Min(Mathf.CeilToInt(coverageEnd) - 1, inputLength - 1);
                    float[] weights = new float[lastSourceIndex - firstSourceIndex + 1];
                    for (int sourceIndex = firstSourceIndex; sourceIndex <= lastSourceIndex; sourceIndex++)
                    {
                        float overlap = Mathf.Min(sourceIndex + 1f, coverageEnd) - Mathf.Max(sourceIndex, coverageStart);
                        weights[sourceIndex - firstSourceIndex] = Mathf.Max(overlap, 0f) / inputPerOutput;
                    }
                    firstSourceIndices[outputIndex] = firstSourceIndex;
                    sourceWeights[outputIndex] = weights;
                }
                else
                {
                    float sourcePosition = (outputIndex + 0.5f) * inputPerOutput - 0.5f;
                    int lowerIndex = Mathf.Clamp(Mathf.FloorToInt(sourcePosition), 0, inputLength - 1);
                    int upperIndex = Mathf.Min(lowerIndex + 1, inputLength - 1);
                    float upperWeight = Mathf.Clamp01(sourcePosition - lowerIndex);
                    firstSourceIndices[outputIndex] = lowerIndex;
                    sourceWeights[outputIndex] = upperIndex == lowerIndex
                        ? new[] { 1f }
                        : new[] { 1f - upperWeight, upperWeight };
                }
            }
        }

        private static Color32[] ConvertToStraightSrgb(float[] premultipliedLinear, int pixelCount)
        {
            Color32[] pixels = new Color32[pixelCount];
            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                int channelIndex = pixelIndex * 4;
                float alpha = Mathf.Clamp01(premultipliedLinear[channelIndex + 3]);
                byte alphaByte = (byte)Mathf.RoundToInt(alpha * 255f);
                if (alphaByte == 0)
                {
                    pixels[pixelIndex] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float inverseAlpha = 1f / alpha;
                pixels[pixelIndex] = new Color32(
                    LinearToSrgbByte(premultipliedLinear[channelIndex] * inverseAlpha),
                    LinearToSrgbByte(premultipliedLinear[channelIndex + 1] * inverseAlpha),
                    LinearToSrgbByte(premultipliedLinear[channelIndex + 2] * inverseAlpha),
                    alphaByte);
            }
            return pixels;
        }

        private static void BleedEdgeColors(Color32[] pixels, int width, int height, int passCount)
        {
            bool[] hasColor = new bool[pixels.Length];
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                hasColor[pixelIndex] = pixels[pixelIndex].a > 0;
            }

            List<int> newlyColoredPixels = new List<int>();
            for (int passIndex = 0; passIndex < passCount; passIndex++)
            {
                newlyColoredPixels.Clear();
                for (int rowIndex = 0; rowIndex < height; rowIndex++)
                {
                    for (int columnIndex = 0; columnIndex < width; columnIndex++)
                    {
                        int pixelIndex = rowIndex * width + columnIndex;
                        if (hasColor[pixelIndex])
                        {
                            continue;
                        }

                        int red = 0, green = 0, blue = 0, neighbourCount = 0;
                        for (int rowOffset = -1; rowOffset <= 1; rowOffset++)
                        {
                            int neighbourRow = rowIndex + rowOffset;
                            if (neighbourRow < 0 || neighbourRow >= height) continue;
                            for (int columnOffset = -1; columnOffset <= 1; columnOffset++)
                            {
                                int neighbourColumn = columnIndex + columnOffset;
                                if (neighbourColumn < 0 || neighbourColumn >= width) continue;
                                int neighbourIndex = neighbourRow * width + neighbourColumn;
                                if (!hasColor[neighbourIndex]) continue;
                                Color32 neighbour = pixels[neighbourIndex];
                                red += neighbour.r;
                                green += neighbour.g;
                                blue += neighbour.b;
                                neighbourCount++;
                            }
                        }

                        if (neighbourCount > 0)
                        {
                            pixels[pixelIndex] = new Color32((byte)(red / neighbourCount), (byte)(green / neighbourCount), (byte)(blue / neighbourCount), 0);
                            newlyColoredPixels.Add(pixelIndex);
                        }
                    }
                }

                if (newlyColoredPixels.Count == 0)
                {
                    break;
                }
                for (int listIndex = 0; listIndex < newlyColoredPixels.Count; listIndex++)
                {
                    hasColor[newlyColoredPixels[listIndex]] = true;
                }
            }
        }

        private static void ConfigureOutputImporter(DoodleStickerSpriteSourceImage sourceImage, DoodleStickerSpriteResizePlan plan, DoodleStickerSpriteResizeSettings settings, string outputPath)
        {
            TextureImporter sourceImporter = sourceImage.Importer;
            TextureImporter outputImporter = (TextureImporter)AssetImporter.GetAtPath(outputPath);

            TextureImporterSettings textureSettings = new TextureImporterSettings();
            sourceImporter.ReadTextureSettings(textureSettings);
            textureSettings.textureType = TextureImporterType.Sprite;
            textureSettings.spriteMode = (int)SpriteImportMode.Single;
            textureSettings.readable = false;
            textureSettings.alphaIsTransparency = true;
            textureSettings.npotScale = TextureImporterNPOTScale.None;

            Vector2 pivotInContent = sourceImage.PivotInPixels - new Vector2(plan.ContentRect.x, plan.ContentRect.y);
            Vector2 pivotOnCanvas = new Vector2(plan.ContentOffset.x, plan.ContentOffset.y) + Vector2.Scale(pivotInContent, plan.Scale);
            textureSettings.spriteAlignment = (int)SpriteAlignment.Custom;
            textureSettings.spritePivot = new Vector2(pivotOnCanvas.x / plan.CanvasSize.x, pivotOnCanvas.y / plan.CanvasSize.y);

            float pixelsPerUnitScale = settings.keepWorldSize ? (plan.Scale.x + plan.Scale.y) * 0.5f : 1f;
            textureSettings.spritePixelsPerUnit = Mathf.Max(sourceImage.PixelsPerUnit * pixelsPerUnitScale, 0.0001f);
            outputImporter.SetTextureSettings(textureSettings);

            int requiredMaximumSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(plan.CanvasSize.x, plan.CanvasSize.y)), 32, 16384);
            TextureImporterPlatformSettings defaultPlatformSettings = sourceImporter.GetDefaultPlatformTextureSettings();
            defaultPlatformSettings.maxTextureSize = requiredMaximumSize;
            outputImporter.SetPlatformTextureSettings(defaultPlatformSettings);

            for (int platformIndex = 0; platformIndex < PlatformNames.Length; platformIndex++)
            {
                TextureImporterPlatformSettings platformSettings = sourceImporter.GetPlatformTextureSettings(PlatformNames[platformIndex]);
                if (!platformSettings.overridden)
                {
                    continue;
                }
                platformSettings.maxTextureSize = requiredMaximumSize;
                outputImporter.SetPlatformTextureSettings(platformSettings);
            }

            if (settings.mobileCompression != DoodleStickerMobileCompression.KeepSource)
            {
                for (int platformIndex = 0; platformIndex < MobilePlatformNames.Length; platformIndex++)
                {
                    TextureImporterPlatformSettings mobileSettings = outputImporter.GetPlatformTextureSettings(MobilePlatformNames[platformIndex]);
                    mobileSettings.overridden = true;
                    mobileSettings.maxTextureSize = requiredMaximumSize;
                    mobileSettings.format = ToTextureImporterFormat(settings.mobileCompression);
                    outputImporter.SetPlatformTextureSettings(mobileSettings);
                }
            }

            outputImporter.SaveAndReimport();
        }

        private static TextureImporterFormat ToTextureImporterFormat(DoodleStickerMobileCompression mobileCompression)
        {
            switch (mobileCompression)
            {
                case DoodleStickerMobileCompression.Astc4x4: return TextureImporterFormat.ASTC_4x4;
                case DoodleStickerMobileCompression.Astc5x5: return TextureImporterFormat.ASTC_5x5;
                case DoodleStickerMobileCompression.Astc6x6: return TextureImporterFormat.ASTC_6x6;
                default: return TextureImporterFormat.ASTC_8x8;
            }
        }

        private static RectInt FindOpaqueBounds(Color32[] pixels, int width, int height)
        {
            int minimumX = width, minimumY = height, maximumX = -1, maximumY = -1;
            for (int rowIndex = 0; rowIndex < height; rowIndex++)
            {
                int rowStart = rowIndex * width;
                for (int columnIndex = 0; columnIndex < width; columnIndex++)
                {
                    if (pixels[rowStart + columnIndex].a < OpaqueAlphaThreshold) continue;
                    if (columnIndex < minimumX) minimumX = columnIndex;
                    if (columnIndex > maximumX) maximumX = columnIndex;
                    if (rowIndex < minimumY) minimumY = rowIndex;
                    if (rowIndex > maximumY) maximumY = rowIndex;
                }
            }
            return maximumX < 0 ? new RectInt(0, 0, 0, 0) : new RectInt(minimumX, minimumY, maximumX - minimumX + 1, maximumY - minimumY + 1);
        }

        private static Color32[] ReadImportedTexturePixels(Texture2D importedTexture)
        {
            RenderTexture readbackTarget = RenderTexture.GetTemporary(importedTexture.width, importedTexture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previousActive = RenderTexture.active;
            Texture2D readbackTexture = new Texture2D(importedTexture.width, importedTexture.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(importedTexture, readbackTarget);
                RenderTexture.active = readbackTarget;
                readbackTexture.ReadPixels(new Rect(0, 0, importedTexture.width, importedTexture.height), 0, 0);
                readbackTexture.Apply(false);
                return readbackTexture.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(readbackTarget);
                UnityEngine.Object.DestroyImmediate(readbackTexture);
            }
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parentFolder = Path.GetDirectoryName(folderPath).Replace('\\', '/');
            EnsureFolderExists(parentFolder);
            AssetDatabase.CreateFolder(parentFolder, Path.GetFileName(folderPath));
        }

        private static float[] BuildSrgbToLinearTable()
        {
            float[] table = new float[256];
            for (int byteValue = 0; byteValue < 256; byteValue++)
            {
                float srgb = byteValue / 255f;
                table[byteValue] = srgb <= 0.04045f ? srgb / 12.92f : Mathf.Pow((srgb + 0.055f) / 1.055f, 2.4f);
            }
            return table;
        }

        private static byte LinearToSrgbByte(float linear)
        {
            linear = Mathf.Clamp01(linear);
            float srgb = linear <= 0.0031308f ? linear * 12.92f : 1.055f * Mathf.Pow(linear, 1f / 2.4f) - 0.055f;
            return (byte)Mathf.RoundToInt(srgb * 255f);
        }
    }
}
