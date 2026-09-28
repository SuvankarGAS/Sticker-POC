using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DoodleStickers
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Doodle Stickers/Doodle Sticker")]
    public sealed class DoodleSticker : MonoBehaviour,
        IInitializePotentialDragHandler, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private static readonly List<DoodleSticker> ActiveStickerList = new List<DoodleSticker>();
        private static readonly List<DoodleStickerIssue> IssueBuffer = new List<DoodleStickerIssue>();
        private static bool hasWarnedAboutMissingEventSystem;
        private static Material fallbackTemplateMaterial;

        [SerializeField] private Sprite sprite;
        [SerializeField] private Color color = Color.white;
        [SerializeField] private bool flipX;
        [SerializeField] private bool flipY;
        [SerializeField] private Material stickerMaterial;
        [SerializeField] private int sortingLayerId;
        [SerializeField] private int sortingOrder;

        [Tooltip("Whether the pointer can peel this sticker.")]
        [SerializeField] private bool interactable = true;

        [Tooltip("Which part of the sprite counts as touchable.")]
        [SerializeField] private DoodleStickerHitTestMode hitTestMode = DoodleStickerHitTestMode.PhysicsShape;

        [Tooltip("Adds a Doodle Sticker Raycaster to the main camera at runtime if no camera has one.")]
        [SerializeField] private bool addRaycasterToMainCamera = true;

        [Tooltip("Scaled time pauses with Time.timeScale; unscaled time keeps animating.")]
        [SerializeField] private DoodleStickerTimeMode timeMode = DoodleStickerTimeMode.Scaled;

        [Tooltip("What happens to this GameObject after the sticker has fully peeled off.")]
        [SerializeField] private DoodleStickerPeeledOffAction peeledOffAction = DoodleStickerPeeledOffAction.None;

        [SerializeField] private DoodleStickerPeel peel = new DoodleStickerPeel();

        private readonly DoodleStickerSpriteShape shape = new DoodleStickerSpriteShape();
        private readonly List<Vector2> stickerPoints = new List<Vector2>();
        private readonly List<int> triangleIndices = new List<int>();
        private readonly List<Vector3> vertexPositions = new List<Vector3>();
        private readonly List<Color32> vertexColors = new List<Color32>();
        private readonly List<Vector4> vertexAtlasUVRects = new List<Vector4>();
        private readonly List<Vector4> vertexCurlPoints = new List<Vector4>();
        private readonly List<Vector2> vertexCurlSpaceScales = new List<Vector2>();

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh stickerMesh;
        private Material acquiredMaterial;
        private Material acquiredTemplate;
        private Texture acquiredTexture;
        private Texture acquiredAlphaTexture;
        private System.Func<Vector2, bool> sheetContainsSourcePoint;
        private bool isShapeDirty = true;
        private bool isMeshDirty = true;
        private bool hasAppliedPeeledOffAction;
        private int appliedSortingLayerId = int.MinValue;
        private int appliedSortingOrder = int.MinValue;

        public static IReadOnlyList<DoodleSticker> ActiveStickers => ActiveStickerList;

        public Sprite Sprite
        {
            get => sprite;
            set
            {
                if (sprite == value) return;
                sprite = value;
                isShapeDirty = true;
            }
        }

        public Color Color
        {
            get => color;
            set
            {
                color = value;
                isMeshDirty = true;
            }
        }

        public bool FlipX
        {
            get => flipX;
            set
            {
                flipX = value;
                isMeshDirty = true;
            }
        }

        public bool FlipY
        {
            get => flipY;
            set
            {
                flipY = value;
                isMeshDirty = true;
            }
        }

        public Material StickerMaterial
        {
            get => stickerMaterial;
            set
            {
                stickerMaterial = value;
                isMeshDirty = true;
            }
        }

        public int SortingLayerId
        {
            get => sortingLayerId;
            set => sortingLayerId = value;
        }

        public int SortingOrder
        {
            get => sortingOrder;
            set => sortingOrder = value;
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

        private Material EffectiveTemplateMaterial => stickerMaterial != null ? stickerMaterial : GetFallbackTemplateMaterial();

        private void OnEnable()
        {
            CacheComponents();
            if (stickerMesh == null)
            {
                stickerMesh = new Mesh { name = "Doodle Sticker Mesh", hideFlags = HideFlags.HideAndDontSave };
                stickerMesh.MarkDynamic();
            }
            meshFilter.sharedMesh = stickerMesh;
            meshRenderer.enabled = true;

            peel.ResetToInitialPose();
            hasAppliedPeeledOffAction = false;
            isShapeDirty = true;
            isMeshDirty = true;
            ActiveStickerList.Add(this);

            ApplyPendingChanges();

            if (Application.isPlaying)
            {
                EnsureEventSetup();
                IssueBuffer.Clear();
                CollectIssues(IssueBuffer);
                DoodleStickerValidation.LogIssues(this, IssueBuffer);
            }
        }

        private void OnDisable()
        {
            peel.CancelPointer();
            ActiveStickerList.Remove(this);
            ReleaseMaterial();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
            }
        }

        private void OnDestroy()
        {
            DoodleStickerObjectUtility.DestroySafely(stickerMesh);
        }

        private void OnValidate()
        {
            isShapeDirty = true;
            isMeshDirty = true;
            if (!Application.isPlaying)
            {
                peel.ResetToInitialPose();
            }

            if (isActiveAndEnabled && stickerMesh != null)
            {
                shape.Rebuild(sprite);
                isShapeDirty = false;
                RebuildMesh();
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall -= ApplyPendingChangesIfAlive;
            UnityEditor.EditorApplication.delayCall += ApplyPendingChangesIfAlive;
#endif
        }

        private void LateUpdate()
        {
            if (!ReferenceEquals(shape.Sprite, sprite))
            {
                isShapeDirty = true;
            }

            if (Application.isPlaying && peel.Tick(DoodleStickerShaderTime.GetDeltaTime(timeMode)))
            {
                isMeshDirty = true;
            }

            DoodleStickerShaderTime.Publish();
            if (isShapeDirty || isMeshDirty || !IsMaterialCurrent() || appliedSortingLayerId != sortingLayerId || appliedSortingOrder != sortingOrder)
            {
                ApplyPendingChanges();
            }

            if (Application.isPlaying && peel.IsPeeledOff && !hasAppliedPeeledOffAction)
            {
                hasAppliedPeeledOffAction = true;
                DoodleStickerObjectUtility.ApplyPeeledOffAction(gameObject, peeledOffAction);
            }
        }

        public void Flatten()
        {
            peel.Flatten();
            isMeshDirty = true;
        }

        public void FlattenSmoothly()
        {
            peel.FlattenSmoothly();
            isMeshDirty = true;
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
            ApplyPendingChanges();
            if (!shape.IsValid)
            {
                return false;
            }

            DoodleStickerCurlSettings curlSettings = DoodleStickerCurlSettings.FromMaterial(EffectiveTemplateMaterial);
            LastAutomaticPeelPlan = DoodleStickerAutomaticPeelPlanner.PlanRandomEdgePeel(
                shape, hitTestMode, liftedAmount, curlSettings.CurlRadius, peel.AutomaticPeelAngleVariation, peel.AutomaticEdgeMargin);

            bool hasStarted = peel.BeginAutomaticPeel(LastAutomaticPeelPlan.GrabPoint, LastAutomaticPeelPlan.DragPoint, duration, peelOffWhenDone, onCompleted);
            isMeshDirty = true;
            return hasStarted;
        }

        public void FlattenAutomatically()
        {
            FlattenAutomatically(peel.AutomaticFlattenStartAmount, peel.FlattenDuration);
        }

        public bool FlattenAutomatically(float startLiftedAmount, float duration, System.Action onCompleted = null)
        {
            ApplyPendingChanges();
            if (!shape.IsValid || peel.IsPeeledOff || peel.State == DoodleStickerPeelState.PeelingOff)
            {
                return false;
            }

            if (peel.IsFlat)
            {
                DoodleStickerCurlSettings curlSettings = DoodleStickerCurlSettings.FromMaterial(EffectiveTemplateMaterial);
                LastAutomaticPeelPlan = DoodleStickerAutomaticPeelPlanner.PlanRandomEdgePeel(
                    shape, hitTestMode, startLiftedAmount, curlSettings.CurlRadius, peel.AutomaticPeelAngleVariation, peel.AutomaticEdgeMargin);
                peel.SetPose(LastAutomaticPeelPlan.GrabPoint, LastAutomaticPeelPlan.DragPoint);
            }

            peel.FlattenSmoothly(duration, onCompleted);
            isMeshDirty = true;
            return true;
        }

        public void ResetToInitialPose()
        {
            peel.ResetToInitialPose();
            hasAppliedPeeledOffAction = false;
            isMeshDirty = true;
        }

        public void RefreshNow()
        {
            isShapeDirty = true;
            isMeshDirty = true;
            ApplyPendingChanges();
        }

        public void CollectIssues(List<DoodleStickerIssue> issues)
        {
            DoodleStickerValidation.CollectSpriteIssues(sprite, issues);
            if (stickerMaterial == null)
            {
                issues.Add(new DoodleStickerIssue(DoodleStickerIssueSeverity.Warning,
                    "No material is assigned, so a temporary one is used. Assign a material that uses \"" + DoodleStickerShaderIds.SpriteShaderName + "\" so the shader is included in builds and can be tuned."));
            }
            else
            {
                DoodleStickerValidation.CollectMaterialIssues(stickerMaterial, DoodleStickerShaderIds.SpriteShaderName, issues);
            }
        }

        public Vector3 StickerPointToWorld(Vector2 stickerPoint)
        {
            Vector2 localPoint = shape.StickerToLocalPoint(stickerPoint);
            return transform.TransformPoint(ApplyFlip(localPoint));
        }

        public bool TryGetStickerPoint(Ray worldRay, out Vector2 stickerPoint, out float distance, out Vector3 worldPoint)
        {
            stickerPoint = default;
            worldPoint = default;
            Plane stickerPlane = new Plane(transform.forward, transform.position);
            if (!stickerPlane.Raycast(worldRay, out distance))
            {
                return false;
            }

            worldPoint = worldRay.GetPoint(distance);
            Vector2 localPoint = transform.InverseTransformPoint(worldPoint);
            stickerPoint = shape.LocalToStickerPoint(ApplyFlip(localPoint));
            return !float.IsNaN(stickerPoint.x) && !float.IsNaN(stickerPoint.y);
        }

        public bool TryGetStickerPoint(Vector2 screenPosition, Camera camera, out Vector2 stickerPoint)
        {
            stickerPoint = default;
            return camera != null && TryGetStickerPoint(camera.ScreenPointToRay(screenPosition), out stickerPoint, out _, out _);
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

        public bool TryRaycast(Ray worldRay, out float distance, out Vector3 worldPoint)
        {
            worldPoint = default;
            distance = 0f;
            if (!interactable || !isActiveAndEnabled)
            {
                return false;
            }

            return TryGetStickerPoint(worldRay, out Vector2 stickerPoint, out distance, out worldPoint)
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
                isMeshDirty = true;
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
                isMeshDirty = true;
            }
        }

        private void ApplyPendingChangesIfAlive()
        {
            if (this != null && isActiveAndEnabled)
            {
                ApplyPendingChanges();
            }
        }

        private void ApplyPendingChanges()
        {
            CacheComponents();

            if (isShapeDirty)
            {
                shape.Rebuild(sprite);
                isShapeDirty = false;
                isMeshDirty = true;
            }

            RefreshMaterial();
            if (appliedSortingLayerId != sortingLayerId || appliedSortingOrder != sortingOrder)
            {
                meshRenderer.sortingLayerID = sortingLayerId;
                meshRenderer.sortingOrder = sortingOrder;
                appliedSortingLayerId = sortingLayerId;
                appliedSortingOrder = sortingOrder;
            }

            if (isMeshDirty)
            {
                RebuildMesh();
                isMeshDirty = false;
            }
        }

        private void CacheComponents()
        {
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        }

        private bool IsMaterialCurrent()
        {
            return !ReferenceEquals(acquiredMaterial, null)
                && ReferenceEquals(acquiredTemplate, stickerMaterial != null ? stickerMaterial : fallbackTemplateMaterial)
                && ReferenceEquals(acquiredTexture, shape.Texture)
                && ReferenceEquals(acquiredAlphaTexture, shape.AlphaTexture);
        }

        private void RefreshMaterial()
        {
            Material templateMaterial = EffectiveTemplateMaterial;
            bool isMaterialCurrent = acquiredMaterial != null
                && acquiredTemplate == templateMaterial
                && acquiredTexture == shape.Texture
                && acquiredAlphaTexture == shape.AlphaTexture;
            if (isMaterialCurrent)
            {
                return;
            }

            ReleaseMaterial();
            acquiredTemplate = templateMaterial;
            acquiredTexture = shape.Texture;
            acquiredAlphaTexture = shape.AlphaTexture;
            acquiredMaterial = DoodleStickerMaterialCache.Acquire(templateMaterial, shape.Texture, shape.AlphaTexture);
            meshRenderer.sharedMaterial = acquiredMaterial;
        }

        private void ReleaseMaterial()
        {
            if (acquiredMaterial != null)
            {
                DoodleStickerMaterialCache.Release(acquiredMaterial);
            }
            acquiredMaterial = null;
            acquiredTemplate = null;
            acquiredTexture = null;
            acquiredAlphaTexture = null;
        }

        private void RebuildMesh()
        {
            stickerMesh.Clear();
            if (!shape.IsValid || peel.IsPeeledOff)
            {
                return;
            }

            DoodleStickerCurlSettings curlSettings = DoodleStickerCurlSettings.FromMaterial(EffectiveTemplateMaterial);
            DoodleStickerGeometryBuilder.BuildStickerGeometry(shape, peel, curlSettings, stickerPoints, triangleIndices);

            Color vertexColor = color;
            vertexColor.a *= peel.Opacity;
            Color32 vertexColor32 = vertexColor;
            Vector4 curlPoints = new Vector4(peel.GrabPoint.x, peel.GrabPoint.y, peel.DragPoint.x, peel.DragPoint.y);

            vertexPositions.Clear();
            vertexColors.Clear();
            vertexAtlasUVRects.Clear();
            vertexCurlPoints.Clear();
            vertexCurlSpaceScales.Clear();

            for (int pointIndex = 0; pointIndex < stickerPoints.Count; pointIndex++)
            {
                vertexPositions.Add(ApplyFlip(shape.StickerToLocalPoint(stickerPoints[pointIndex])));
                vertexColors.Add(vertexColor32);
                vertexAtlasUVRects.Add(shape.AtlasUVRect);
                vertexCurlPoints.Add(curlPoints);
                vertexCurlSpaceScales.Add(shape.CurlSpaceScale);
            }

            stickerMesh.SetVertices(vertexPositions);
            stickerMesh.SetColors(vertexColors);
            stickerMesh.SetUVs(0, stickerPoints);
            stickerMesh.SetUVs(1, vertexAtlasUVRects);
            stickerMesh.SetUVs(2, vertexCurlPoints);
            stickerMesh.SetUVs(3, vertexCurlSpaceScales);
            stickerMesh.SetTriangles(triangleIndices, 0, true);
        }

        private Vector2 ApplyFlip(Vector2 localPoint)
        {
            return new Vector2(flipX ? -localPoint.x : localPoint.x, flipY ? -localPoint.y : localPoint.y);
        }

        private void EnsureEventSetup()
        {
            if (addRaycasterToMainCamera && DoodleStickerObjectUtility.FindFirst<DoodleStickerRaycaster>() == null)
            {
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    mainCamera.gameObject.AddComponent<DoodleStickerRaycaster>();
                }
                else
                {
                    Debug.LogWarning("[Doodle Sticker] No camera is tagged MainCamera, so no Doodle Sticker Raycaster was added. Add one to your camera manually.", this);
                }
            }

            if (!hasWarnedAboutMissingEventSystem && EventSystem.current == null && DoodleStickerObjectUtility.FindFirst<EventSystem>() == null)
            {
                hasWarnedAboutMissingEventSystem = true;
                Debug.LogWarning("[Doodle Sticker] The scene has no EventSystem, so stickers cannot receive pointer input. Add one with GameObject > UI > Event System.", this);
            }
        }

        private static Material GetFallbackTemplateMaterial()
        {
            if (fallbackTemplateMaterial == null)
            {
                Shader stickerShader = Shader.Find(DoodleStickerShaderIds.SpriteShaderName);
                if (stickerShader != null)
                {
                    fallbackTemplateMaterial = new Material(stickerShader) { name = "Doodle Sticker (Fallback)", hideFlags = HideFlags.HideAndDontSave };
                }
            }
            return fallbackTemplateMaterial;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            ActiveStickerList.Clear();
            hasWarnedAboutMissingEventSystem = false;
        }
    }
}
