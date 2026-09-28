using UnityEngine;

namespace DoodleStickers
{
    public enum DoodleStickerEdge
    {
        Left,
        Right,
        Bottom,
        Top
    }

    public readonly struct DoodleStickerAutomaticPeelPlan
    {
        public readonly DoodleStickerEdge Edge;
        public readonly Vector2 GrabPoint;
        public readonly Vector2 DragPoint;

        public DoodleStickerAutomaticPeelPlan(DoodleStickerEdge edge, Vector2 grabPoint, Vector2 dragPoint)
        {
            Edge = edge;
            GrabPoint = grabPoint;
            DragPoint = dragPoint;
        }
    }

    public static class DoodleStickerAutomaticPeelPlanner
    {
        private const int EdgeSearchAttempts = 12;
        private const int InwardSearchSteps = 256;
        private const int InsideArtStepCount = 2;
        private const int PeelDistanceSearchIterations = 24;
        private const float MaximumPeelDistanceInStickerLengths = 3f;
        private const float MinimumPeelDistanceForMeasuring = 1e-3f;

        public static DoodleStickerAutomaticPeelPlan PlanRandomEdgePeel(
            DoodleStickerSpriteShape shape,
            DoodleStickerHitTestMode hitTestMode,
            float liftedAmount,
            float maximumCurlRadius,
            float angleVariationDegrees,
            float edgeMargin)
        {
            Rect artBounds = EvaluateArtBounds(shape);
            if (!TryFindRandomEdgePoint(shape, hitTestMode, artBounds, edgeMargin, out DoodleStickerEdge edge, out Vector2 grabPoint, out Vector2 inwardDirection))
            {
                edge = (DoodleStickerEdge)Random.Range(0, 4);
                GetEdgeStart(artBounds, edge, 0.5f, out grabPoint, out inwardDirection);
            }

            Vector2 curlSpaceScale = shape.CurlSpaceScale;
            Vector2 inwardCurlDirection = Vector2.Scale(inwardDirection, curlSpaceScale).normalized;
            float tiltRadians = Random.Range(-angleVariationDegrees, angleVariationDegrees) * Mathf.Deg2Rad;
            float tiltCosine = Mathf.Cos(tiltRadians);
            float tiltSine = Mathf.Sin(tiltRadians);
            Vector2 peelCurlDirection = new Vector2(
                inwardCurlDirection.x * tiltCosine - inwardCurlDirection.y * tiltSine,
                inwardCurlDirection.x * tiltSine + inwardCurlDirection.y * tiltCosine);

            float peelDistance = FindPeelDistanceForLiftedAmount(grabPoint, peelCurlDirection, curlSpaceScale, maximumCurlRadius, artBounds, Mathf.Clamp01(liftedAmount));
            Vector2 dragPoint = grabPoint + new Vector2(
                peelCurlDirection.x * peelDistance / curlSpaceScale.x,
                peelCurlDirection.y * peelDistance / curlSpaceScale.y);

            return new DoodleStickerAutomaticPeelPlan(edge, grabPoint, dragPoint);
        }

        public static Rect EvaluateArtBounds(DoodleStickerSpriteShape shape)
        {
            Vector2[] extentPoints = shape.SheetExtentPoints;
            if (extentPoints == null || extentPoints.Length < 3)
            {
                return new Rect(0f, 0f, 1f, 1f);
            }

            Vector2 minimum = extentPoints[0];
            Vector2 maximum = extentPoints[0];
            for (int pointIndex = 1; pointIndex < extentPoints.Length; pointIndex++)
            {
                minimum = Vector2.Min(minimum, extentPoints[pointIndex]);
                maximum = Vector2.Max(maximum, extentPoints[pointIndex]);
            }

            minimum = Vector2.Max(minimum, Vector2.zero);
            maximum = Vector2.Min(maximum, Vector2.one);
            if (maximum.x - minimum.x < 1e-4f || maximum.y - minimum.y < 1e-4f)
            {
                return new Rect(0f, 0f, 1f, 1f);
            }
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        private static bool TryFindRandomEdgePoint(
            DoodleStickerSpriteShape shape,
            DoodleStickerHitTestMode hitTestMode,
            Rect artBounds,
            float edgeMargin,
            out DoodleStickerEdge edge,
            out Vector2 grabPoint,
            out Vector2 inwardDirection)
        {
            float clampedMargin = Mathf.Clamp(edgeMargin, 0f, 0.49f);
            for (int attemptIndex = 0; attemptIndex < EdgeSearchAttempts; attemptIndex++)
            {
                edge = (DoodleStickerEdge)Random.Range(0, 4);
                float positionAlongEdge = Random.Range(clampedMargin, 1f - clampedMargin);
                GetEdgeStart(artBounds, edge, positionAlongEdge, out Vector2 edgeStart, out inwardDirection);

                float searchLength = edge == DoodleStickerEdge.Left || edge == DoodleStickerEdge.Right ? artBounds.width : artBounds.height;
                float stepLength = searchLength / InwardSearchSteps;
                for (int stepIndex = 0; stepIndex <= InwardSearchSteps; stepIndex++)
                {
                    Vector2 candidatePoint = edgeStart + inwardDirection * (stepLength * stepIndex);
                    if (!shape.Contains(candidatePoint, hitTestMode))
                    {
                        continue;
                    }

                    Vector2 insideArtPoint = candidatePoint + inwardDirection * (stepLength * InsideArtStepCount);
                    grabPoint = shape.Contains(insideArtPoint, hitTestMode) ? insideArtPoint : candidatePoint;
                    return true;
                }
            }

            edge = DoodleStickerEdge.Right;
            grabPoint = default;
            inwardDirection = default;
            return false;
        }

        private static void GetEdgeStart(Rect artBounds, DoodleStickerEdge edge, float positionAlongEdge, out Vector2 edgeStart, out Vector2 inwardDirection)
        {
            switch (edge)
            {
                case DoodleStickerEdge.Left:
                    edgeStart = new Vector2(artBounds.xMin, Mathf.Lerp(artBounds.yMin, artBounds.yMax, positionAlongEdge));
                    inwardDirection = Vector2.right;
                    break;
                case DoodleStickerEdge.Right:
                    edgeStart = new Vector2(artBounds.xMax, Mathf.Lerp(artBounds.yMin, artBounds.yMax, positionAlongEdge));
                    inwardDirection = Vector2.left;
                    break;
                case DoodleStickerEdge.Bottom:
                    edgeStart = new Vector2(Mathf.Lerp(artBounds.xMin, artBounds.xMax, positionAlongEdge), artBounds.yMin);
                    inwardDirection = Vector2.up;
                    break;
                default:
                    edgeStart = new Vector2(Mathf.Lerp(artBounds.xMin, artBounds.xMax, positionAlongEdge), artBounds.yMax);
                    inwardDirection = Vector2.down;
                    break;
            }
        }

        private static float FindPeelDistanceForLiftedAmount(
            Vector2 grabPoint,
            Vector2 peelCurlDirection,
            Vector2 curlSpaceScale,
            float maximumCurlRadius,
            Rect artBounds,
            float targetLiftedAmount)
        {
            float shortestDistance = 0f;
            float longestDistance = MaximumPeelDistanceInStickerLengths * Mathf.Max(curlSpaceScale.x, curlSpaceScale.y);
            float liftedAtGrabPoint = EvaluateLiftedAmount(grabPoint, peelCurlDirection, MinimumPeelDistanceForMeasuring, curlSpaceScale, maximumCurlRadius, artBounds);
            float requiredLiftedAmount = Mathf.Lerp(liftedAtGrabPoint, 1f, targetLiftedAmount);

            for (int iterationIndex = 0; iterationIndex < PeelDistanceSearchIterations; iterationIndex++)
            {
                float candidateDistance = (shortestDistance + longestDistance) * 0.5f;
                float liftedAmount = EvaluateLiftedAmount(grabPoint, peelCurlDirection, candidateDistance, curlSpaceScale, maximumCurlRadius, artBounds);

                if (liftedAmount < requiredLiftedAmount)
                {
                    shortestDistance = candidateDistance;
                }
                else
                {
                    longestDistance = candidateDistance;
                }
            }
            return longestDistance;
        }

        private static float EvaluateLiftedAmount(Vector2 grabPoint, Vector2 peelCurlDirection, float peelDistance, Vector2 curlSpaceScale, float maximumCurlRadius, Rect artBounds)
        {
            Vector2 dragPoint = grabPoint + new Vector2(
                peelCurlDirection.x * peelDistance / curlSpaceScale.x,
                peelCurlDirection.y * peelDistance / curlSpaceScale.y);

            return DoodleStickerFold
                .Create(grabPoint, dragPoint, curlSpaceScale, maximumCurlRadius)
                .EvaluateLiftedFraction(artBounds);
        }
    }
}
