using UnityEngine;

namespace DoodleStickers
{
    public enum DoodleStickerSurfaceRegion
    {
        None,
        Stuck,
        Lifted
    }

    public readonly struct DoodleStickerFold
    {
        public const float MinimumPeelDistance = 1e-4f;

        private static readonly Vector2[] StickerRectCorners =
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };

        public readonly Vector2 Origin;
        public readonly Vector2 Axis;
        public readonly float Radius;
        public readonly Vector2 CurlSpaceScale;
        public readonly bool IsFlat;

        private DoodleStickerFold(Vector2 origin, Vector2 axis, float radius, Vector2 curlSpaceScale, bool isFlat)
        {
            Origin = origin;
            Axis = axis;
            Radius = radius;
            CurlSpaceScale = curlSpaceScale;
            IsFlat = isFlat;
        }

        public static DoodleStickerFold Create(Vector2 grabStickerPoint, Vector2 dragStickerPoint, Vector2 curlSpaceScale, float maximumCurlRadius)
        {
            Vector2 grabCurlPoint = Vector2.Scale(grabStickerPoint, curlSpaceScale);
            Vector2 dragCurlPoint = Vector2.Scale(dragStickerPoint, curlSpaceScale);
            Vector2 peelVector = grabCurlPoint - dragCurlPoint;
            float peelDistance = peelVector.magnitude;

            if (peelDistance < MinimumPeelDistance)
            {
                return new DoodleStickerFold(Vector2.zero, Vector2.right, MinimumPeelDistance, curlSpaceScale, true);
            }

            Vector2 axis = peelVector / peelDistance;
            float radius = Mathf.Max(Mathf.Min(maximumCurlRadius, peelDistance / Mathf.PI), MinimumPeelDistance);
            Vector2 origin = grabCurlPoint - axis * ((peelDistance + Mathf.PI * radius) * 0.5f);
            return new DoodleStickerFold(origin, axis, radius, curlSpaceScale, false);
        }

        public Vector2 StickerToCurlSpace(Vector2 stickerPoint) => Vector2.Scale(stickerPoint, CurlSpaceScale);

        public Vector2 CurlToStickerSpace(Vector2 curlPoint) => new Vector2(curlPoint.x / CurlSpaceScale.x, curlPoint.y / CurlSpaceScale.y);

        public float DistanceFromFold(Vector2 curlPoint) => Vector2.Dot(curlPoint - Origin, Axis);

        public void EvaluateSheetLayers(Vector2 stickerPoint, out float distanceFromFold, out Vector2 lowerSourceStickerPoint, out Vector2 upperSourceStickerPoint)
        {
            Vector2 curlPoint = StickerToCurlSpace(stickerPoint);
            distanceFromFold = DistanceFromFold(curlPoint);

            float curlAngle = Mathf.Asin(Mathf.Clamp01(distanceFromFold / Radius));
            Vector2 pointOnFoldLine = curlPoint - distanceFromFold * Axis;
            float stuckDistance = Mathf.Min(distanceFromFold, 0f);
            float curlArcLength = curlAngle * Radius;

            lowerSourceStickerPoint = CurlToStickerSpace(pointOnFoldLine + Axis * (stuckDistance + curlArcLength));
            upperSourceStickerPoint = CurlToStickerSpace(pointOnFoldLine + Axis * (Mathf.PI * Radius - stuckDistance - curlArcLength));
        }

        public DoodleStickerSurfaceRegion HitTest(Vector2 stickerPoint, System.Func<Vector2, bool> sheetContainsSourcePoint)
        {
            if (IsFlat)
            {
                return sheetContainsSourcePoint(stickerPoint) ? DoodleStickerSurfaceRegion.Stuck : DoodleStickerSurfaceRegion.None;
            }

            EvaluateSheetLayers(stickerPoint, out float distanceFromFold, out Vector2 lowerSourcePoint, out Vector2 upperSourcePoint);
            if (distanceFromFold > Radius)
            {
                return DoodleStickerSurfaceRegion.None;
            }

            if (sheetContainsSourcePoint(upperSourcePoint))
            {
                return DoodleStickerSurfaceRegion.Lifted;
            }

            if (!sheetContainsSourcePoint(lowerSourcePoint))
            {
                return DoodleStickerSurfaceRegion.None;
            }

            return distanceFromFold > 0f ? DoodleStickerSurfaceRegion.Lifted : DoodleStickerSurfaceRegion.Stuck;
        }

        public float EvaluateLiftedFraction() => EvaluateLiftedFraction(new Rect(0f, 0f, 1f, 1f));

        public float EvaluateLiftedFraction(Rect stickerBounds)
        {
            if (IsFlat)
            {
                return 0f;
            }

            float minimumDistance = float.MaxValue;
            float maximumDistance = float.MinValue;
            for (int cornerIndex = 0; cornerIndex < StickerRectCorners.Length; cornerIndex++)
            {
                Vector2 boundsCorner = stickerBounds.min + Vector2.Scale(StickerRectCorners[cornerIndex], stickerBounds.size);
                float cornerDistance = DistanceFromFold(StickerToCurlSpace(boundsCorner));
                minimumDistance = Mathf.Min(minimumDistance, cornerDistance);
                maximumDistance = Mathf.Max(maximumDistance, cornerDistance);
            }

            float distanceSpan = maximumDistance - minimumDistance;
            return distanceSpan > 0f ? Mathf.Clamp01(maximumDistance / distanceSpan) : 0f;
        }

        public Rect EvaluateCoveredStickerRect(float edgePadding, Vector2 shadowOffset, float shadowSpread)
        {
            Vector2 coveredMinimum = Vector2.zero;
            Vector2 coveredMaximum = CurlSpaceScale;

            if (!IsFlat && TryEvaluateLiftedSheetExtents(out Vector2[] liftedSheetCorners))
            {
                for (int cornerIndex = 0; cornerIndex < liftedSheetCorners.Length; cornerIndex++)
                {
                    Vector2 liftedCorner = liftedSheetCorners[cornerIndex];
                    Vector2 shadowCorner = liftedCorner + shadowOffset;
                    coveredMinimum = Vector2.Min(coveredMinimum, Vector2.Min(liftedCorner, shadowCorner - Vector2.one * shadowSpread));
                    coveredMaximum = Vector2.Max(coveredMaximum, Vector2.Max(liftedCorner, shadowCorner + Vector2.one * shadowSpread));
                }

                coveredMinimum -= Vector2.one * shadowSpread;
                coveredMaximum += Vector2.one * shadowSpread;
            }

            coveredMinimum -= Vector2.one * edgePadding;
            coveredMaximum += Vector2.one * edgePadding;

            Vector2 stickerMinimum = CurlToStickerSpace(coveredMinimum);
            Vector2 stickerMaximum = CurlToStickerSpace(coveredMaximum);
            return Rect.MinMaxRect(stickerMinimum.x, stickerMinimum.y, stickerMaximum.x, stickerMaximum.y);
        }

        private bool TryEvaluateLiftedSheetExtents(out Vector2[] liftedSheetCorners)
        {
            liftedSheetCorners = null;
            Vector2 foldOrigin = Origin;
            Vector2 lateralAxis = new Vector2(-Axis.y, Axis.x);

            float minimumLateral = float.MaxValue;
            float maximumLateral = float.MinValue;
            float maximumLiftedDistance = float.MinValue;
            bool hasLiftedPart = false;

            for (int cornerIndex = 0; cornerIndex < StickerRectCorners.Length; cornerIndex++)
            {
                Vector2 currentCorner = StickerToCurlSpace(StickerRectCorners[cornerIndex]);
                Vector2 nextCorner = StickerToCurlSpace(StickerRectCorners[(cornerIndex + 1) % StickerRectCorners.Length]);
                float currentDistance = DistanceFromFold(currentCorner);
                float nextDistance = DistanceFromFold(nextCorner);

                if (currentDistance >= 0f)
                {
                    IncludeLiftedPoint(currentCorner, currentDistance);
                }

                if ((currentDistance >= 0f) != (nextDistance >= 0f))
                {
                    float crossingFraction = currentDistance / (currentDistance - nextDistance);
                    IncludeLiftedPoint(Vector2.Lerp(currentCorner, nextCorner, crossingFraction), 0f);
                }
            }

            if (!hasLiftedPart)
            {
                return false;
            }

            float nearestAxialExtent = Mathf.Min(0f, Mathf.PI * Radius - maximumLiftedDistance);
            float farthestAxialExtent = Mathf.Min(Radius, maximumLiftedDistance);

            liftedSheetCorners = new[]
            {
                Origin + Axis * nearestAxialExtent + lateralAxis * minimumLateral,
                Origin + Axis * nearestAxialExtent + lateralAxis * maximumLateral,
                Origin + Axis * farthestAxialExtent + lateralAxis * minimumLateral,
                Origin + Axis * farthestAxialExtent + lateralAxis * maximumLateral
            };
            return true;

            void IncludeLiftedPoint(Vector2 curlPoint, float distanceFromFold)
            {
                float lateral = Vector2.Dot(curlPoint - foldOrigin, lateralAxis);
                minimumLateral = Mathf.Min(minimumLateral, lateral);
                maximumLateral = Mathf.Max(maximumLateral, lateral);
                maximumLiftedDistance = Mathf.Max(maximumLiftedDistance, distanceFromFold);
                hasLiftedPart = true;
            }
        }
    }
}
