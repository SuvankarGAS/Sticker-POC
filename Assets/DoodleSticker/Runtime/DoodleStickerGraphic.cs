using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DoodleStickers
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("Doodle Stickers/Doodle Sticker (UI)")]
    public sealed class DoodleStickerGraphic : MaskableGraphic, ICanvasRaycastFilter,
        IInitializePotentialDragHandler, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private const AdditionalCanvasShaderChannels RequiredShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;

        private static readonly List<DoodleStickerIssue> IssueBuffer = new List<DoodleStickerIssue>();
        private static Material fallbackMaterial;

        [SerializeField] private Sprite sprite;
        [SerializeField] private bool preserveAspect = true;

        [Tooltip("Whether the pointer can peel this sticker.")]
        [SerializeField] private bool interactable = true;

        [Tooltip("Which part of the sprite counts as touchable.")]
        [SerializeField] private DoodleStickerHitTestMode hitTestMode = DoodleStickerHitTestMode.PhysicsShape;

        [Tooltip("Scaled time pauses with Time.timeScale; unscaled time keeps animating.")]
        [SerializeField] private DoodleStickerTimeMode timeMode = DoodleStickerTimeMode.Unscaled;

        [Tooltip("What happens to this GameObject after the sticker has fully peeled off.")]
        [SerializeField] private DoodleStickerPeeledOffAction peeledOffAction = DoodleStickerPeeledOffAction.None;

        [SerializeField] private DoodleStickerPeel peel = new DoodleStickerPeel();

        private readonly DoodleStickerSpriteShape shape = new DoodleStickerSpriteShape();
        private readonly List<Vector2> stickerPoints = new List<Vector2>();
        private readonly List<int> triangleIndices = new List<int>();
        private System.Func<Vector2, bool> sheetContainsSourcePoint;
        private bool isShapeDirty = true;
        private bool hasAppliedPeeledOffAction;

        public Sprite Sprite
        {
            get => sprite;
            set
            {
                if (sprite == value) return;
                sprite = value;
                isShapeDirty = true;
                SetAllDirty();
            }
        }

        public bool PreserveAspect
        {
            get => preserveAspect;
            set
            {
                preserveAspect = value;
                SetVerticesDirty();
            }
        }

        public bool Interactable
        {
            get => interactable;
            set
            {
                interactable = value;
                if (!interactable) peel.CancelPointer();
            }
        }

        public DoodleStickerHitTestMode HitTestMode
        {
            get => hitTestMode;
            set => hitTestMode = value;
        }

        public DoodleStickerPeel Peel => peel;
        public DoodleStickerSpriteShape Shape => shape;
        public DoodleStickerFold CurrentFold => peel.CreateFold();
        public DoodleStickerAutomaticPeelPlan LastAutomaticPeelPlan { get; private set; }

        public override Texture mainTexture => sprite != null && sprite.texture != null ? sprite.texture : s_WhiteTexture;

        public override Material defaultMaterial
        {
            get
            {
                if (fallbackMaterial == null)
                {
                    Shader graphicShader = Shader.Find(DoodleStickerShaderIds.GraphicShaderName);
                    if (graphicShader == null)
                    {
                        return base.defaultMaterial;
                    }
                    fallbackMaterial = new Material(graphicShader) { name = "Doodle Sticker UI (Fallback)", hideFlags = HideFlags.HideAndDontSave };
                }
                return fallbackMaterial;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            peel.ResetToInitialPose();
            hasAppliedPeeledOffAction = false;
            isShapeDirty = true;
            EnsureCanvasShaderChannels();

            if (Application.isPlaying)
            {
                IssueBuffer.Clear();
                CollectIssues(IssueBuffer);
                DoodleStickerValidation.LogIssues(this, IssueBuffer);
            }
        }

        protected override void OnDisable()
        {
            peel.CancelPointer();
            base.OnDisable();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            isShapeDirty = true;
            if (!Application.isPlaying)
            {
                peel.ResetToInitialPose();
            }
        }
#endif

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnsureCanvasShaderChannels();
        }

        private void Update()
        {
            if (shape.Sprite != sprite)
            {
                isShapeDirty = true;
                SetVerticesDirty();
            }

            if (Application.isPlaying && peel.Tick(DoodleStickerShaderTime.GetDeltaTime(timeMode)))
            {
                SetVerticesDirty();
            }

            DoodleStickerShaderTime.Publish();

            if (Application.isPlaying && peel.IsPeeledOff && !hasAppliedPeeledOffAction)
            {
                hasAppliedPeeledOffAction = true;
                DoodleStickerObjectUtility.ApplyPeeledOffAction(gameObject, peeledOffAction);
            }
        }

        public void Flatten()
        {
            peel.Flatten();
            SetVerticesDirty();
        }

        public void FlattenSmoothly()
        {
            peel.FlattenSmoothly();
            SetVerticesDirty();
        }

        public void PeelAutomatically()
        {
            PeelAutomatically(peel.AutomaticPeelAmount, peel.AutomaticPeelDuration);
        }

        public void PeelOffAutomatically()
        {
            PeelAutomatically(1f, peel.AutomaticPeelDuration, true);
        }

        public bool PeelAutomatically(float liftedAmount, float duration, bool peelOffWhenDone = false, System.Action onCompleted = null)
        {
            if (!PrepareAutomaticGeometry(out DoodleStickerCurlSettings curlSettings))
            {
                return false;
            }

            LastAutomaticPeelPlan = DoodleStickerAutomaticPeelPlanner.PlanRandomEdgePeel(
                shape, hitTestMode, liftedAmount, curlSettings.CurlRadius, peel.AutomaticPeelAngleVariation, peel.AutomaticEdgeMargin);

            bool hasStarted = peel.BeginAutomaticPeel(LastAutomaticPeelPlan.GrabPoint, LastAutomaticPeelPlan.DragPoint, duration, peelOffWhenDone, onCompleted);
            SetVerticesDirty();
            return hasStarted;
        }

        public void FlattenAutomatically()
        {
            FlattenAutomatically(peel.AutomaticFlattenStartAmount, peel.FlattenDuration);
        }

        public bool FlattenAutomatically(float startLiftedAmount, float duration, System.Action onCompleted = null)
        {
            if (!PrepareAutomaticGeometry(out DoodleStickerCurlSettings curlSettings) || peel.IsPeeledOff || peel.State == DoodleStickerPeelState.PeelingOff)
            {
                return false;
            }

            if (peel.IsFlat)
            {
                LastAutomaticPeelPlan = DoodleStickerAutomaticPeelPlanner.PlanRandomEdgePeel(
                    shape, hitTestMode, startLiftedAmount, curlSettings.CurlRadius, peel.AutomaticPeelAngleVariation, peel.AutomaticEdgeMargin);
                peel.SetPose(LastAutomaticPeelPlan.GrabPoint, LastAutomaticPeelPlan.DragPoint);
            }

            peel.FlattenSmoothly(duration, onCompleted);
            SetVerticesDirty();
            return true;
        }

        private bool PrepareAutomaticGeometry(out DoodleStickerCurlSettings curlSettings)
        {
            RebuildShapeIfDirty();
            curlSettings = DoodleStickerCurlSettings.FromMaterial(material);
            if (!shape.IsValid)
            {
                return false;
            }

            peel.SetGeometry(shape.CurlSpaceScale, curlSettings.CurlRadius, shape.SheetExtentPoints);
            return true;
        }

        public void ResetToInitialPose()
        {
            peel.ResetToInitialPose();
            hasAppliedPeeledOffAction = false;
            SetVerticesDirty();
        }

        public override void SetNativeSize()
        {
            if (sprite == null)
            {
                return;
            }

            float referencePixelsPerUnit = canvas != null ? canvas.referencePixelsPerUnit : 100f;
            float spritePixelsPerCanvasUnit = sprite.pixelsPerUnit / referencePixelsPerUnit;
            rectTransform.anchorMax = rectTransform.anchorMin;
            rectTransform.sizeDelta = sprite.rect.size / spritePixelsPerCanvasUnit;
            SetAllDirty();
        }

        public void CollectIssues(List<DoodleStickerIssue> issues)
        {
            DoodleStickerValidation.CollectSpriteIssues(sprite, issues);
            if (m_Material == null)
            {
                issues.Add(new DoodleStickerIssue(DoodleStickerIssueSeverity.Warning,
                    "No material is assigned, so a temporary one is used. Assign a material that uses \"" + DoodleStickerShaderIds.GraphicShaderName + "\" so the shader is included in builds and can be tuned."));
            }
            else
            {
                DoodleStickerValidation.CollectMaterialIssues(m_Material, DoodleStickerShaderIds.GraphicShaderName, issues);
            }
            if (!raycastTarget && interactable)
            {
                issues.Add(new DoodleStickerIssue(DoodleStickerIssueSeverity.Warning, "Raycast Target is off, so the sticker cannot be peeled with the pointer."));
            }
        }

        public Rect GetStickerDrawRect()
        {
            Rect drawRect = GetPixelAdjustedRect();
            if (!preserveAspect || sprite == null)
            {
                return drawRect;
            }

            RebuildShapeIfDirty();
            Vector2 spriteSize = shape.SpriteLocalRect.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f || drawRect.width <= 0f || drawRect.height <= 0f)
            {
                return drawRect;
            }

            float spriteAspect = spriteSize.x / spriteSize.y;
            float rectAspect = drawRect.width / drawRect.height;
            Vector2 pivot = rectTransform.pivot;

            if (spriteAspect > rectAspect)
            {
                float fullHeight = drawRect.height;
                drawRect.height = drawRect.width / spriteAspect;
                drawRect.y += (fullHeight - drawRect.height) * pivot.y;
            }
            else
            {
                float fullWidth = drawRect.width;
                drawRect.width = drawRect.height * spriteAspect;
                drawRect.x += (fullWidth - drawRect.width) * pivot.x;
            }
            return drawRect;
        }

        public Vector3 StickerPointToWorld(Vector2 stickerPoint)
        {
            Rect drawRect = GetStickerDrawRect();
            return rectTransform.TransformPoint(drawRect.min + Vector2.Scale(stickerPoint, drawRect.size));
        }

        public bool TryGetStickerPoint(Vector2 screenPosition, Camera eventCamera, out Vector2 stickerPoint)
        {
            stickerPoint = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPosition, eventCamera, out Vector2 localPoint))
            {
                return false;
            }

            Rect drawRect = GetStickerDrawRect();
            if (drawRect.width <= 0f || drawRect.height <= 0f)
            {
                return false;
            }

            stickerPoint = new Vector2((localPoint.x - drawRect.xMin) / drawRect.width, (localPoint.y - drawRect.yMin) / drawRect.height);
            return true;
        }

        public bool CanStartPeelAt(Vector2 stickerPoint)
        {
            DoodleStickerSurfaceRegion touchedRegion = HitTest(stickerPoint);
            if (touchedRegion == DoodleStickerSurfaceRegion.Lifted)
            {
                return true;
            }
            return touchedRegion == DoodleStickerSurfaceRegion.Stuck && peel.CanStartNewPeelAt(shape.EvaluateGrabEdge(stickerPoint, hitTestMode));
        }

        public DoodleStickerSurfaceRegion HitTest(Vector2 stickerPoint)
        {
            RebuildShapeIfDirty();
            if (!shape.IsValid || peel.IsPeeledOff)
            {
                return DoodleStickerSurfaceRegion.None;
            }

            if (sheetContainsSourcePoint == null)
            {
                sheetContainsSourcePoint = sourcePoint => shape.Contains(sourcePoint, hitTestMode);
            }
            return peel.CreateFold().HitTest(stickerPoint, sheetContainsSourcePoint);
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            return interactable
                && TryGetStickerPoint(screenPoint, eventCamera, out Vector2 stickerPoint)
                && HitTest(stickerPoint) != DoodleStickerSurfaceRegion.None;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!interactable || !TryGetStickerPoint(eventData.position, eventData.pressEventCamera, out Vector2 stickerPoint))
            {
                return;
            }

            DoodleStickerSurfaceRegion touchedRegion = HitTest(stickerPoint);
            DoodleStickerGrabEdge grabEdge = touchedRegion == DoodleStickerSurfaceRegion.Stuck
                ? shape.EvaluateGrabEdge(stickerPoint, hitTestMode)
                : default;
            if (peel.TryBeginPointer(eventData.pointerId, stickerPoint, touchedRegion, grabEdge))
            {
                SetVerticesDirty();
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (TryGetStickerPoint(eventData.position, eventData.pressEventCamera, out Vector2 stickerPoint))
            {
                peel.MovePointer(eventData.pointerId, stickerPoint);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (peel.ReleasePointer(eventData.pointerId))
            {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            RebuildShapeIfDirty();
            if (!shape.IsValid || peel.IsPeeledOff)
            {
                return;
            }

            DoodleStickerCurlSettings curlSettings = DoodleStickerCurlSettings.FromMaterial(material);
            DoodleStickerGeometryBuilder.BuildStickerGeometry(shape, peel, curlSettings, stickerPoints, triangleIndices);

            Rect drawRect = GetStickerDrawRect();
            Color vertexColor = color;
            vertexColor.a *= peel.Opacity;
            Vector4 curlPoints = new Vector4(peel.GrabPoint.x, peel.GrabPoint.y, peel.DragPoint.x, peel.DragPoint.y);
            Vector4 curlSpaceScale = new Vector4(shape.CurlSpaceScale.x, shape.CurlSpaceScale.y, 0f, 0f);

            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = vertexColor;
            vertex.uv1 = shape.AtlasUVRect;
            vertex.uv2 = curlPoints;
            vertex.uv3 = curlSpaceScale;

            for (int pointIndex = 0; pointIndex < stickerPoints.Count; pointIndex++)
            {
                Vector2 stickerPoint = stickerPoints[pointIndex];
                vertex.position = drawRect.min + Vector2.Scale(stickerPoint, drawRect.size);
                vertex.uv0 = new Vector4(stickerPoint.x, stickerPoint.y, 0f, 0f);
                vertexHelper.AddVert(vertex);
            }

            for (int triangleStart = 0; triangleStart + 2 < triangleIndices.Count; triangleStart += 3)
            {
                vertexHelper.AddTriangle(triangleIndices[triangleStart], triangleIndices[triangleStart + 1], triangleIndices[triangleStart + 2]);
            }

            UpdateRaycastPaddingToCoverSticker(drawRect);
        }

        private void UpdateRaycastPaddingToCoverSticker(Rect drawRect)
        {
            Rect coveredStickerRect = DoodleStickerGeometryBuilder.EvaluateStickerPointBounds(stickerPoints);
            Rect graphicRect = rectTransform.rect;
            Vector2 coveredMinimum = drawRect.min + Vector2.Scale(coveredStickerRect.min, drawRect.size);
            Vector2 coveredMaximum = drawRect.min + Vector2.Scale(coveredStickerRect.max, drawRect.size);

            raycastPadding = new Vector4(
                coveredMinimum.x - graphicRect.xMin,
                coveredMinimum.y - graphicRect.yMin,
                graphicRect.xMax - coveredMaximum.x,
                graphicRect.yMax - coveredMaximum.y);
        }

        private void RebuildShapeIfDirty()
        {
            if (!isShapeDirty && shape.Sprite == sprite)
            {
                return;
            }

            shape.Rebuild(sprite);
            isShapeDirty = false;
        }

        private void EnsureCanvasShaderChannels()
        {
            Canvas parentCanvas = canvas;
            if (parentCanvas == null)
            {
                return;
            }

            if ((parentCanvas.additionalShaderChannels & RequiredShaderChannels) != RequiredShaderChannels)
            {
                parentCanvas.additionalShaderChannels |= RequiredShaderChannels;
            }

            Canvas rootCanvas = parentCanvas.rootCanvas;
            if (rootCanvas != null && (rootCanvas.additionalShaderChannels & RequiredShaderChannels) != RequiredShaderChannels)
            {
                rootCanvas.additionalShaderChannels |= RequiredShaderChannels;
            }
        }
    }
}
