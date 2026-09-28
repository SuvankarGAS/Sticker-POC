using System.Collections.Generic;
using UnityEngine;

namespace DoodleStickers
{
    public enum DoodleStickerHitTestMode
    {
        SpriteRect,
        SpriteMesh,
        PhysicsShape
    }

    public readonly struct DoodleStickerGrabEdge
    {
        public readonly bool IsValid;
        public readonly float DistanceFromEdge;
        public readonly Vector2 InwardDirection;

        public DoodleStickerGrabEdge(float distanceFromEdge, Vector2 inwardDirection)
        {
            IsValid = true;
            DistanceFromEdge = distanceFromEdge;
            InwardDirection = inwardDirection;
        }
    }

    public sealed class DoodleStickerSpriteShape
    {
        private const float OnEdgeDistance = 1e-5f;
        private const float OutsideProbeDistance = 1e-3f;

        private static readonly List<Vector2> PhysicsShapeBuffer = new List<Vector2>();

        private readonly List<Vector2[]> physicsOutlines = new List<Vector2[]>();
        private Vector2[] meshStickerPoints = System.Array.Empty<Vector2>();
        private ushort[] meshTriangles = System.Array.Empty<ushort>();
        private Vector2[] sheetExtentPoints = System.Array.Empty<Vector2>();
        private Vector2[] physicsEdgeSegments = System.Array.Empty<Vector2>();
        private Vector2[] meshEdgeSegments = System.Array.Empty<Vector2>();
        private static readonly Vector2[] RectEdgeSegments =
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 0f)
        };

        public Sprite Sprite { get; private set; }
        public bool IsValid { get; private set; }
        public Rect SpriteLocalRect { get; private set; } = new Rect(-0.5f, -0.5f, 1f, 1f);
        public Vector4 AtlasUVRect { get; private set; } = new Vector4(0f, 0f, 1f, 1f);
        public Vector2 CurlSpaceScale { get; private set; } = Vector2.one;
        public Texture Texture { get; private set; }
        public Texture AlphaTexture { get; private set; }
        public IReadOnlyList<Vector2> MeshStickerPoints => meshStickerPoints;
        public IReadOnlyList<ushort> MeshTriangles => meshTriangles;
        public bool HasPhysicsShape => physicsOutlines.Count > 0;
        public Vector2[] SheetExtentPoints => sheetExtentPoints;

        public void Rebuild(Sprite sprite)
        {
            Sprite = sprite;
            physicsOutlines.Clear();
            meshStickerPoints = System.Array.Empty<Vector2>();
            meshTriangles = System.Array.Empty<ushort>();
            sheetExtentPoints = System.Array.Empty<Vector2>();
            physicsEdgeSegments = System.Array.Empty<Vector2>();
            meshEdgeSegments = System.Array.Empty<Vector2>();
            IsValid = false;
            Texture = null;
            AlphaTexture = null;

            if (sprite == null)
            {
                SpriteLocalRect = new Rect(-0.5f, -0.5f, 1f, 1f);
                AtlasUVRect = new Vector4(0f, 0f, 1f, 1f);
                CurlSpaceScale = Vector2.one;
                return;
            }

            float pixelsPerUnit = Mathf.Max(sprite.pixelsPerUnit, 1e-5f);
            Vector2 localSize = sprite.rect.size / pixelsPerUnit;
            if (localSize.x <= 0f || localSize.y <= 0f)
            {
                return;
            }

            SpriteLocalRect = new Rect(-sprite.pivot / pixelsPerUnit, localSize);
            CurlSpaceScale = localSize / Mathf.Max(localSize.x, localSize.y);
            Texture = sprite.texture;
            AlphaTexture = sprite.associatedAlphaSplitTexture;
            AtlasUVRect = EvaluateAtlasUVRect(sprite, SpriteLocalRect);

            Vector2[] spriteVertices = sprite.vertices;
            meshStickerPoints = new Vector2[spriteVertices.Length];
            for (int vertexIndex = 0; vertexIndex < spriteVertices.Length; vertexIndex++)
            {
                meshStickerPoints[vertexIndex] = LocalToStickerPoint(spriteVertices[vertexIndex]);
            }
            meshTriangles = sprite.triangles;

            int physicsShapeCount = sprite.GetPhysicsShapeCount();
            for (int shapeIndex = 0; shapeIndex < physicsShapeCount; shapeIndex++)
            {
                PhysicsShapeBuffer.Clear();
                sprite.GetPhysicsShape(shapeIndex, PhysicsShapeBuffer);
                if (PhysicsShapeBuffer.Count < 3)
                {
                    continue;
                }

                Vector2[] outline = new Vector2[PhysicsShapeBuffer.Count];
                for (int pointIndex = 0; pointIndex < outline.Length; pointIndex++)
                {
                    outline[pointIndex] = LocalToStickerPoint(PhysicsShapeBuffer[pointIndex]);
                }
                physicsOutlines.Add(outline);
            }

            sheetExtentPoints = BuildSheetExtentPoints();
            physicsEdgeSegments = BuildPhysicsEdgeSegments();
            meshEdgeSegments = BuildMeshEdgeSegments();
            IsValid = Texture != null;
        }

        public DoodleStickerGrabEdge EvaluateGrabEdge(Vector2 stickerPoint, DoodleStickerHitTestMode hitTestMode)
        {
            Vector2[] edgeSegments = GetEdgeSegments(hitTestMode);
            Vector2 curlPoint = Vector2.Scale(stickerPoint, CurlSpaceScale);
            float nearestDistance = float.MaxValue;
            Vector2 nearestCurlPoint = curlPoint;
            Vector2 nearestSegmentDirection = Vector2.right;

            for (int segmentIndex = 0; segmentIndex + 1 < edgeSegments.Length; segmentIndex += 2)
            {
                Vector2 segmentStart = Vector2.Scale(edgeSegments[segmentIndex], CurlSpaceScale);
                Vector2 segmentEnd = Vector2.Scale(edgeSegments[segmentIndex + 1], CurlSpaceScale);
                Vector2 segment = segmentEnd - segmentStart;
                float segmentLengthSquared = segment.sqrMagnitude;
                float along = segmentLengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(curlPoint - segmentStart, segment) / segmentLengthSquared) : 0f;
                Vector2 closestPoint = segmentStart + segment * along;
                float distance = (curlPoint - closestPoint).magnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestCurlPoint = closestPoint;
                    nearestSegmentDirection = segmentLengthSquared > 0f ? segment / Mathf.Sqrt(segmentLengthSquared) : Vector2.right;
                }
            }

            if (nearestDistance == float.MaxValue)
            {
                return default;
            }

            Vector2 outwardDirection;
            if (nearestDistance > OnEdgeDistance)
            {
                outwardDirection = (nearestCurlPoint - curlPoint) / nearestDistance;
            }
            else
            {
                outwardDirection = new Vector2(-nearestSegmentDirection.y, nearestSegmentDirection.x);
                Vector2 probePoint = curlPoint + outwardDirection * OutsideProbeDistance;
                if (Contains(new Vector2(probePoint.x / CurlSpaceScale.x, probePoint.y / CurlSpaceScale.y), hitTestMode))
                {
                    outwardDirection = -outwardDirection;
                }
            }

            return new DoodleStickerGrabEdge(nearestDistance, -outwardDirection);
        }

        private Vector2[] GetEdgeSegments(DoodleStickerHitTestMode hitTestMode)
        {
            switch (hitTestMode)
            {
                case DoodleStickerHitTestMode.PhysicsShape when physicsEdgeSegments.Length > 0:
                    return physicsEdgeSegments;
                case DoodleStickerHitTestMode.PhysicsShape:
                case DoodleStickerHitTestMode.SpriteMesh:
                    return meshEdgeSegments.Length > 0 ? meshEdgeSegments : RectEdgeSegments;
                default:
                    return RectEdgeSegments;
            }
        }

        private Vector2[] BuildPhysicsEdgeSegments()
        {
            List<Vector2> segmentPoints = new List<Vector2>();
            for (int outlineIndex = 0; outlineIndex < physicsOutlines.Count; outlineIndex++)
            {
                Vector2[] outline = physicsOutlines[outlineIndex];
                for (int pointIndex = 0; pointIndex < outline.Length; pointIndex++)
                {
                    segmentPoints.Add(outline[pointIndex]);
                    segmentPoints.Add(outline[(pointIndex + 1) % outline.Length]);
                }
            }
            return segmentPoints.ToArray();
        }

        private Vector2[] BuildMeshEdgeSegments()
        {
            Dictionary<long, int> edgeUseCounts = new Dictionary<long, int>();
            for (int triangleStart = 0; triangleStart + 2 < meshTriangles.Length; triangleStart += 3)
            {
                for (int cornerIndex = 0; cornerIndex < 3; cornerIndex++)
                {
                    long edgeKey = MakeEdgeKey(meshTriangles[triangleStart + cornerIndex], meshTriangles[triangleStart + (cornerIndex + 1) % 3]);
                    edgeUseCounts.TryGetValue(edgeKey, out int useCount);
                    edgeUseCounts[edgeKey] = useCount + 1;
                }
            }

            List<Vector2> segmentPoints = new List<Vector2>();
            foreach (KeyValuePair<long, int> edgeUse in edgeUseCounts)
            {
                if (edgeUse.Value != 1)
                {
                    continue;
                }
                segmentPoints.Add(meshStickerPoints[(int)(edgeUse.Key >> 32)]);
                segmentPoints.Add(meshStickerPoints[(int)(edgeUse.Key & 0xFFFFFFFF)]);
            }
            return segmentPoints.ToArray();
        }

        private static long MakeEdgeKey(int firstVertexIndex, int secondVertexIndex)
        {
            int lowerIndex = Mathf.Min(firstVertexIndex, secondVertexIndex);
            int upperIndex = Mathf.Max(firstVertexIndex, secondVertexIndex);
            return ((long)lowerIndex << 32) | (uint)upperIndex;
        }

        private Vector2[] BuildSheetExtentPoints()
        {
            if (physicsOutlines.Count == 0)
            {
                return meshStickerPoints.Length > 0 ? meshStickerPoints : System.Array.Empty<Vector2>();
            }

            List<Vector2> outlinePoints = new List<Vector2>();
            for (int outlineIndex = 0; outlineIndex < physicsOutlines.Count; outlineIndex++)
            {
                outlinePoints.AddRange(physicsOutlines[outlineIndex]);
            }
            return outlinePoints.ToArray();
        }

        public Vector2 LocalToStickerPoint(Vector2 spriteLocalPoint)
        {
            Rect localRect = SpriteLocalRect;
            return new Vector2(
                (spriteLocalPoint.x - localRect.xMin) / localRect.width,
                (spriteLocalPoint.y - localRect.yMin) / localRect.height);
        }

        public Vector2 StickerToLocalPoint(Vector2 stickerPoint)
        {
            Rect localRect = SpriteLocalRect;
            return localRect.min + Vector2.Scale(stickerPoint, localRect.size);
        }

        public bool Contains(Vector2 stickerPoint, DoodleStickerHitTestMode hitTestMode)
        {
            if (stickerPoint.x < 0f || stickerPoint.x > 1f || stickerPoint.y < 0f || stickerPoint.y > 1f)
            {
                return false;
            }

            switch (hitTestMode)
            {
                case DoodleStickerHitTestMode.PhysicsShape when physicsOutlines.Count > 0:
                    return PhysicsOutlinesContain(stickerPoint);
                case DoodleStickerHitTestMode.PhysicsShape:
                case DoodleStickerHitTestMode.SpriteMesh:
                    return meshTriangles.Length >= 3 ? SpriteMeshContains(stickerPoint) : true;
                default:
                    return true;
            }
        }

        private bool PhysicsOutlinesContain(Vector2 stickerPoint)
        {
            bool isInside = false;
            for (int outlineIndex = 0; outlineIndex < physicsOutlines.Count; outlineIndex++)
            {
                Vector2[] outline = physicsOutlines[outlineIndex];
                for (int pointIndex = 0, previousIndex = outline.Length - 1; pointIndex < outline.Length; previousIndex = pointIndex++)
                {
                    Vector2 currentPoint = outline[pointIndex];
                    Vector2 previousPoint = outline[previousIndex];
                    bool crossesScanline = (currentPoint.y > stickerPoint.y) != (previousPoint.y > stickerPoint.y);
                    if (crossesScanline)
                    {
                        float crossingX = previousPoint.x + (stickerPoint.y - previousPoint.y) * (currentPoint.x - previousPoint.x) / (currentPoint.y - previousPoint.y);
                        if (stickerPoint.x < crossingX)
                        {
                            isInside = !isInside;
                        }
                    }
                }
            }
            return isInside;
        }

        private bool SpriteMeshContains(Vector2 stickerPoint)
        {
            for (int triangleStart = 0; triangleStart + 2 < meshTriangles.Length; triangleStart += 3)
            {
                Vector2 cornerA = meshStickerPoints[meshTriangles[triangleStart]];
                Vector2 cornerB = meshStickerPoints[meshTriangles[triangleStart + 1]];
                Vector2 cornerC = meshStickerPoints[meshTriangles[triangleStart + 2]];

                float edgeAB = Cross(cornerB - cornerA, stickerPoint - cornerA);
                float edgeBC = Cross(cornerC - cornerB, stickerPoint - cornerB);
                float edgeCA = Cross(cornerA - cornerC, stickerPoint - cornerC);
                bool hasNegative = edgeAB < 0f || edgeBC < 0f || edgeCA < 0f;
                bool hasPositive = edgeAB > 0f || edgeBC > 0f || edgeCA > 0f;
                if (!(hasNegative && hasPositive))
                {
                    return true;
                }
            }
            return false;
        }

        private static float Cross(Vector2 first, Vector2 second) => first.x * second.y - first.y * second.x;

        private static Vector4 EvaluateAtlasUVRect(Sprite sprite, Rect spriteLocalRect)
        {
            Vector2[] vertices = sprite.vertices;
            Vector2[] uvs = sprite.uv;

            if (vertices.Length == uvs.Length && vertices.Length >= 3)
            {
                Vector2 vertexMinimum = vertices[0];
                Vector2 vertexMaximum = vertices[0];
                Vector2 uvMinimum = uvs[0];
                Vector2 uvMaximum = uvs[0];
                for (int vertexIndex = 1; vertexIndex < vertices.Length; vertexIndex++)
                {
                    vertexMinimum = Vector2.Min(vertexMinimum, vertices[vertexIndex]);
                    vertexMaximum = Vector2.Max(vertexMaximum, vertices[vertexIndex]);
                    uvMinimum = Vector2.Min(uvMinimum, uvs[vertexIndex]);
                    uvMaximum = Vector2.Max(uvMaximum, uvs[vertexIndex]);
                }

                Vector2 vertexExtent = vertexMaximum - vertexMinimum;
                if (vertexExtent.x > 1e-6f && vertexExtent.y > 1e-6f)
                {
                    Vector2 uvPerLocalUnit = new Vector2(
                        (uvMaximum.x - uvMinimum.x) / vertexExtent.x,
                        (uvMaximum.y - uvMinimum.y) / vertexExtent.y);
                    Vector2 uvAtLocalOrigin = uvMinimum - Vector2.Scale(vertexMinimum, uvPerLocalUnit);
                    Vector2 atlasRectMinimum = uvAtLocalOrigin + Vector2.Scale(spriteLocalRect.min, uvPerLocalUnit);
                    Vector2 atlasRectSize = Vector2.Scale(spriteLocalRect.size, uvPerLocalUnit);
                    return new Vector4(atlasRectMinimum.x, atlasRectMinimum.y, atlasRectSize.x, atlasRectSize.y);
                }
            }

            Texture2D texture = sprite.texture;
            if (texture == null || texture.width == 0 || texture.height == 0)
            {
                return new Vector4(0f, 0f, 1f, 1f);
            }

            Rect pixelRect = sprite.rect;
            return new Vector4(
                pixelRect.x / texture.width,
                pixelRect.y / texture.height,
                pixelRect.width / texture.width,
                pixelRect.height / texture.height);
        }
    }
}
