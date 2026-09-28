using System.Collections.Generic;
using UnityEngine;

namespace DoodleStickers
{
    public enum DoodleStickerTimeMode
    {
        Scaled,
        Unscaled
    }

    public enum DoodleStickerPeeledOffAction
    {
        None,
        DeactivateGameObject,
        DestroyGameObject
    }

    public static class DoodleStickerShaderIds
    {
        public static readonly int MainTexture = Shader.PropertyToID("_MainTex");
        public static readonly int AlphaTexture = Shader.PropertyToID("_AlphaTex");
        public static readonly int PageCurlRadius = Shader.PropertyToID("_PageCurlRadius");
        public static readonly int PageCurlShadowSoftness = Shader.PropertyToID("_PageCurlShadowSoftness");
        public static readonly int PageCurlShadowOffset = Shader.PropertyToID("_PageCurlShadowOffset");
        public static readonly int DoodleWobbleStrength = Shader.PropertyToID("_DoodleWobbleStrength");
        public static readonly int DoodleStickerUnscaledTime = Shader.PropertyToID("_DoodleStickerUnscaledTime");

        public const string ExternalAlphaKeyword = "_DOODLE_STICKER_EXTERNAL_ALPHA";
        public const string SpriteShaderName = "Effects/Doodle Sticker";
        public const string GraphicShaderName = "Effects/Doodle Sticker UI";
    }

    public readonly struct DoodleStickerCurlSettings
    {
        private const float DefaultCurlRadius = 0.12f;
        private const float DefaultShadowSoftness = 0.03f;
        private const float AntialiasPadding = 0.01f;
        private const float ShadowFalloffLengths = 4f;

        public readonly float CurlRadius;
        public readonly float ShadowSoftness;
        public readonly Vector2 ShadowOffset;
        public readonly float WobbleStrength;

        private DoodleStickerCurlSettings(float curlRadius, float shadowSoftness, Vector2 shadowOffset, float wobbleStrength)
        {
            CurlRadius = curlRadius;
            ShadowSoftness = shadowSoftness;
            ShadowOffset = shadowOffset;
            WobbleStrength = wobbleStrength;
        }

        public float EdgePadding => AntialiasPadding + WobbleStrength;
        public float ShadowSpread => ShadowSoftness * ShadowFalloffLengths;

        public static DoodleStickerCurlSettings FromMaterial(Material material)
        {
            if (material == null)
            {
                return new DoodleStickerCurlSettings(DefaultCurlRadius, DefaultShadowSoftness, Vector2.zero, 0f);
            }

            float curlRadius = material.HasProperty(DoodleStickerShaderIds.PageCurlRadius) ? material.GetFloat(DoodleStickerShaderIds.PageCurlRadius) : DefaultCurlRadius;
            float shadowSoftness = material.HasProperty(DoodleStickerShaderIds.PageCurlShadowSoftness) ? material.GetFloat(DoodleStickerShaderIds.PageCurlShadowSoftness) : DefaultShadowSoftness;
            Vector2 shadowOffset = material.HasProperty(DoodleStickerShaderIds.PageCurlShadowOffset) ? (Vector2)material.GetVector(DoodleStickerShaderIds.PageCurlShadowOffset) : Vector2.zero;
            float wobbleStrength = material.HasProperty(DoodleStickerShaderIds.DoodleWobbleStrength) ? material.GetFloat(DoodleStickerShaderIds.DoodleWobbleStrength) : 0f;
            return new DoodleStickerCurlSettings(Mathf.Max(curlRadius, DoodleStickerFold.MinimumPeelDistance), Mathf.Max(shadowSoftness, 1e-4f), shadowOffset, Mathf.Max(wobbleStrength, 0f));
        }
    }

    public static class DoodleStickerGeometryBuilder
    {
        private static readonly Vector2[] QuadCorners =
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f)
        };

        public static DoodleStickerFold BuildStickerGeometry(
            DoodleStickerSpriteShape shape,
            DoodleStickerPeel peel,
            DoodleStickerCurlSettings curlSettings,
            List<Vector2> stickerPoints,
            List<int> triangleIndices)
        {
            stickerPoints.Clear();
            triangleIndices.Clear();

            peel.SetGeometry(shape.CurlSpaceScale, curlSettings.CurlRadius, shape.SheetExtentPoints);
            DoodleStickerFold fold = peel.CreateFold();

            if (fold.IsFlat && shape.MeshStickerPoints.Count >= 3)
            {
                for (int pointIndex = 0; pointIndex < shape.MeshStickerPoints.Count; pointIndex++)
                {
                    stickerPoints.Add(shape.MeshStickerPoints[pointIndex]);
                }
                for (int triangleIndex = 0; triangleIndex < shape.MeshTriangles.Count; triangleIndex++)
                {
                    triangleIndices.Add(shape.MeshTriangles[triangleIndex]);
                }
                return fold;
            }

            Rect coveredStickerRect = fold.EvaluateCoveredStickerRect(curlSettings.EdgePadding, curlSettings.ShadowOffset, curlSettings.ShadowSpread);
            for (int cornerIndex = 0; cornerIndex < QuadCorners.Length; cornerIndex++)
            {
                Vector2 corner = QuadCorners[cornerIndex];
                stickerPoints.Add(new Vector2(
                    Mathf.Lerp(coveredStickerRect.xMin, coveredStickerRect.xMax, corner.x),
                    Mathf.Lerp(coveredStickerRect.yMin, coveredStickerRect.yMax, corner.y)));
            }

            triangleIndices.Add(0);
            triangleIndices.Add(1);
            triangleIndices.Add(2);
            triangleIndices.Add(2);
            triangleIndices.Add(3);
            triangleIndices.Add(0);
            return fold;
        }

        public static Rect EvaluateStickerPointBounds(List<Vector2> stickerPoints)
        {
            if (stickerPoints.Count == 0)
            {
                return new Rect(0f, 0f, 1f, 1f);
            }

            Vector2 minimum = stickerPoints[0];
            Vector2 maximum = stickerPoints[0];
            for (int pointIndex = 1; pointIndex < stickerPoints.Count; pointIndex++)
            {
                minimum = Vector2.Min(minimum, stickerPoints[pointIndex]);
                maximum = Vector2.Max(maximum, stickerPoints[pointIndex]);
            }
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }
    }

    public static class DoodleStickerShaderTime
    {
        private static int lastPublishedFrame = -1;

        public static void Publish()
        {
            if (Application.isPlaying)
            {
                if (lastPublishedFrame == Time.frameCount)
                {
                    return;
                }
                lastPublishedFrame = Time.frameCount;
                Shader.SetGlobalFloat(DoodleStickerShaderIds.DoodleStickerUnscaledTime, Time.unscaledTime);
                return;
            }

            Shader.SetGlobalFloat(DoodleStickerShaderIds.DoodleStickerUnscaledTime, Time.realtimeSinceStartup);
        }

        public static float GetDeltaTime(DoodleStickerTimeMode timeMode)
        {
            return timeMode == DoodleStickerTimeMode.Unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            lastPublishedFrame = -1;
        }
    }

    public static class DoodleStickerObjectUtility
    {
        public static void DestroySafely(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        public static T FindFirst<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        public static void ApplyPeeledOffAction(GameObject stickerObject, DoodleStickerPeeledOffAction peeledOffAction)
        {
            switch (peeledOffAction)
            {
                case DoodleStickerPeeledOffAction.DeactivateGameObject:
                    stickerObject.SetActive(false);
                    break;
                case DoodleStickerPeeledOffAction.DestroyGameObject:
                    Object.Destroy(stickerObject);
                    break;
            }
        }
    }
}
