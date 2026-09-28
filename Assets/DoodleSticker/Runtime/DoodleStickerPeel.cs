using System;
using UnityEngine;
using UnityEngine.Events;

namespace DoodleStickers
{
    public enum DoodleStickerReleaseBehaviour
    {
        Hold,
        SpringBack,
        Fling
    }

    public enum DoodleStickerPeelState
    {
        Resting,
        Dragging,
        Settling,
        SpringingBack,
        Flinging,
        PeelingOff,
        PeeledOff,
        AutomaticallyPeeling
    }

    [Serializable]
    public sealed class DoodleStickerPeel
    {
        private const float PointerVelocityResponseTime = 0.05f;
        private const float SettleDistanceThreshold = 1e-4f;
        private const float FlattenOvershootDistance = 0.01f;
        private const float FlingStopSpeed = 0.02f;
        private const float MaximumSimulationStep = 1f / 120f;
        private const float PeelOffOvershootFraction = 0.25f;

        [Tooltip("What the sticker does when the pointer is released.")]
        [SerializeField] private DoodleStickerReleaseBehaviour releaseBehaviour = DoodleStickerReleaseBehaviour.Hold;

        [Tooltip("Seconds the peel lags behind the pointer. 0 follows the pointer exactly.")]
        [SerializeField, Min(0f)] private float dragSmoothingTime = 0.03f;

        [Tooltip("How strongly the glue resists peeling. 0 = no resistance, 0.9 = very sticky.")]
        [SerializeField, Range(0f, 0.95f)] private float peelResistance = 0f;

        [Tooltip("Longest peel allowed, as a fraction of the sticker's longest side. 0 = unlimited. Dragging further meets increasing resistance.")]
        [SerializeField, Min(0f)] private float maximumPeelDistance = 0f;

        [Tooltip("Seconds the sticker takes to roll back down flat after Spring Back, or when FlattenSmoothly is called.")]
        [SerializeField, Min(0.01f)] private float flattenDuration = 0.35f;

        [Tooltip("Easing of the roll back down. X is time (0..1), Y is how far it has flattened (0..1).")]
        [SerializeField] private AnimationCurve flattenEasing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("How quickly a flung peel slows down, per second.")]
        [SerializeField, Min(0f)] private float flingDeceleration = 6f;

        [Tooltip("Multiplier on the release speed for Fling.")]
        [SerializeField, Min(0f)] private float flingVelocityScale = 1f;

        [Tooltip("Peel the sticker off completely when released with at least this fraction lifted. 0 disables peeling off.")]
        [SerializeField, Range(0f, 1f)] private float peelOffThreshold = 0f;

        [Tooltip("Speed of the peel-off animation, in sticker lengths per second.")]
        [SerializeField, Min(0.01f)] private float peelOffSpeed = 3f;

        [Tooltip("Seconds the sticker takes to fade out once fully peeled off.")]
        [SerializeField, Min(0f)] private float peelOffFadeDuration = 0.2f;

        [Tooltip("Resting grab point, in sticker space (0..1). Equal grab and drag points mean flat.")]
        [SerializeField] private Vector2 initialGrabPoint = Vector2.zero;

        [Tooltip("Resting drag point, in sticker space (0..1).")]
        [SerializeField] private Vector2 initialDragPoint = Vector2.zero;

        [Tooltip("How far PeelAutomatically peels across the sticker from the grabbed edge, from 0 (barely lifted) to 1 (all of it).")]
        [SerializeField, Range(0.05f, 1f)] private float automaticPeelAmount = 0.5f;

        [Tooltip("Seconds PeelAutomatically takes to lift the sticker.")]
        [SerializeField, Min(0.01f)] private float automaticPeelDuration = 0.6f;

        [Tooltip("Easing of the automatic peel. X is time (0..1), Y is how far the peel has travelled (0..1). Values above 1 overshoot.")]
        [SerializeField] private AnimationCurve automaticPeelEasing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("How far across the sticker it is lifted at the start of FlattenAutomatically when the sticker is flat, before it rolls down. 0 = barely lifted, 1 = all of it.")]
        [SerializeField, Range(0.05f, 1f)] private float automaticFlattenStartAmount = 0.45f;

        [Tooltip("Random tilt of the automatic peel direction away from straight across the sticker, in degrees.")]
        [SerializeField, Range(0f, 60f)] private float automaticPeelAngleVariation = 25f;

        [Tooltip("Keeps the random grab point away from the corners: 0 allows any point along the edge, 0.45 stays near the middle.")]
        [SerializeField, Range(0f, 0.45f)] private float automaticEdgeMargin = 0.15f;

        [SerializeField] private UnityEvent onPeelStarted = new UnityEvent();
        [SerializeField] private UnityEvent onPeelReleased = new UnityEvent();
        [SerializeField] private UnityEvent onPeeledOff = new UnityEvent();
        [SerializeField] private UnityEvent onAutomaticPeelCompleted = new UnityEvent();
        [SerializeField] private UnityEvent onFlattened = new UnityEvent();

        [NonSerialized] private Vector2 grabPoint;
        [NonSerialized] private Vector2 rawDragPoint;
        [NonSerialized] private Vector2 targetRawDragPoint;
        [NonSerialized] private Vector2 previousTargetRawDragPoint;
        [NonSerialized] private Vector2 rawDragVelocity;
        [NonSerialized] private Vector2 pointerVelocity;
        [NonSerialized] private Vector2 pointerAnchorPoint;
        [NonSerialized] private Vector2 rawDragAnchorPoint;
        [NonSerialized] private Vector2 releaseDirection;
        [NonSerialized] private Vector2 flattenAxis;
        [NonSerialized] private float flattenLateralOffset;
        [NonSerialized] private float flattenStartPeelDistance;
        [NonSerialized] private float flattenStartFoldPosition;
        [NonSerialized] private float flattenEndFoldPosition;
        [NonSerialized] private float flattenElapsedTime;
        [NonSerialized] private float activeFlattenDuration = 0.35f;
        [NonSerialized] private Action flattenCompletedCallback;
        [NonSerialized] private Vector2 automaticPeelStartPoint;
        [NonSerialized] private Vector2 automaticPeelTargetPoint;
        [NonSerialized] private float automaticPeelElapsedTime;
        [NonSerialized] private float activeAutomaticPeelDuration;
        [NonSerialized] private bool automaticPeelEndsWithPeelOff;
        [NonSerialized] private Action automaticPeelCompletedCallback;
        [NonSerialized] private bool hasPendingAutomaticPeel;
        [NonSerialized] private Vector2 pendingAutomaticPeelGrabPoint;
        [NonSerialized] private Vector2 pendingAutomaticPeelTargetPoint;
        [NonSerialized] private float pendingAutomaticPeelDuration;
        [NonSerialized] private bool pendingAutomaticPeelEndsWithPeelOff;
        [NonSerialized] private Action pendingAutomaticPeelCallback;
        [NonSerialized] private Vector2[] sheetExtentPoints = System.Array.Empty<Vector2>();
        [NonSerialized] private Vector2 peelOffDirection;
        [NonSerialized] private bool isContinuingExistingPeel;
        [NonSerialized] private bool isResistanceBypassed;
        [NonSerialized] private int activePointerId = int.MinValue;
        [NonSerialized] private Vector2 curlSpaceScale = Vector2.one;
        [NonSerialized] private float maximumCurlRadius = 0.12f;

        public DoodleStickerPeelState State { get; private set; }
        public float Opacity { get; private set; } = 1f;
        public Vector2 GrabPoint => grabPoint;
        public Vector2 DragPoint => isResistanceBypassed ? rawDragPoint : ApplyPeelResistance(rawDragPoint);
        public bool IsDragging => State == DoodleStickerPeelState.Dragging;
        public bool IsPeeledOff => State == DoodleStickerPeelState.PeeledOff;
        public bool IsFlat => CreateFold().IsFlat;
        public float LiftedFraction => CreateFold().EvaluateLiftedFraction();
        public UnityEvent OnPeelStarted => onPeelStarted;
        public UnityEvent OnPeelReleased => onPeelReleased;
        public UnityEvent OnPeeledOff => onPeeledOff;
        public UnityEvent OnAutomaticPeelCompleted => onAutomaticPeelCompleted;
        public UnityEvent OnFlattened => onFlattened;
        public float AutomaticPeelAmount => automaticPeelAmount;
        public float AutomaticPeelDuration => automaticPeelDuration;
        public float AutomaticFlattenStartAmount => automaticFlattenStartAmount;
        public float AutomaticPeelAngleVariation => automaticPeelAngleVariation;
        public float AutomaticEdgeMargin => automaticEdgeMargin;
        public float FlattenDuration => flattenDuration;
        public bool IsAnimatingAutomatically => State == DoodleStickerPeelState.AutomaticallyPeeling || hasPendingAutomaticPeel;

        public Vector2 InitialGrabPoint
        {
            get => initialGrabPoint;
            set => initialGrabPoint = value;
        }

        public Vector2 InitialDragPoint
        {
            get => initialDragPoint;
            set => initialDragPoint = value;
        }

        public DoodleStickerReleaseBehaviour ReleaseBehaviour
        {
            get => releaseBehaviour;
            set => releaseBehaviour = value;
        }

        public float PeelOffThreshold
        {
            get => peelOffThreshold;
            set => peelOffThreshold = Mathf.Clamp01(value);
        }

        public void SetGeometry(Vector2 stickerCurlSpaceScale, float stickerMaximumCurlRadius, Vector2[] stickerSheetExtentPoints)
        {
            curlSpaceScale = stickerCurlSpaceScale;
            maximumCurlRadius = stickerMaximumCurlRadius;
            sheetExtentPoints = stickerSheetExtentPoints ?? System.Array.Empty<Vector2>();
        }

        public void FlattenSmoothly(float durationOverride = -1f, Action onCompleted = null)
        {
            if (State == DoodleStickerPeelState.PeelingOff || State == DoodleStickerPeelState.PeeledOff)
            {
                return;
            }

            ClearAutomaticAnimationRequests();
            activePointerId = int.MinValue;
            isContinuingExistingPeel = false;
            flattenCompletedCallback = onCompleted;
            BeginSmoothFlatten(durationOverride);
        }

        public void SetPose(Vector2 poseGrabPoint, Vector2 poseDragPoint)
        {
            ClearAutomaticAnimationRequests();
            activePointerId = int.MinValue;
            isContinuingExistingPeel = false;
            isResistanceBypassed = true;
            grabPoint = poseGrabPoint;
            rawDragPoint = poseDragPoint;
            targetRawDragPoint = poseDragPoint;
            previousTargetRawDragPoint = poseDragPoint;
            rawDragVelocity = Vector2.zero;
            pointerVelocity = Vector2.zero;
            Opacity = 1f;
            State = DoodleStickerPeelState.Resting;
        }

        public bool BeginAutomaticPeel(Vector2 peelGrabPoint, Vector2 peelTargetDragPoint, float duration, bool peelOffWhenDone, Action onCompleted = null)
        {
            if (State == DoodleStickerPeelState.PeelingOff || State == DoodleStickerPeelState.PeeledOff)
            {
                return false;
            }

            ClearAutomaticAnimationRequests();
            activePointerId = int.MinValue;
            isContinuingExistingPeel = false;

            if (!IsFlat)
            {
                hasPendingAutomaticPeel = true;
                pendingAutomaticPeelGrabPoint = peelGrabPoint;
                pendingAutomaticPeelTargetPoint = peelTargetDragPoint;
                pendingAutomaticPeelDuration = duration;
                pendingAutomaticPeelEndsWithPeelOff = peelOffWhenDone;
                pendingAutomaticPeelCallback = onCompleted;
                BeginSmoothFlatten(-1f);
                return true;
            }

            StartAutomaticPeelNow(peelGrabPoint, peelTargetDragPoint, duration, peelOffWhenDone, onCompleted);
            return true;
        }

        private void StartAutomaticPeelNow(Vector2 peelGrabPoint, Vector2 peelTargetDragPoint, float duration, bool peelOffWhenDone, Action onCompleted)
        {
            grabPoint = peelGrabPoint;
            rawDragPoint = peelGrabPoint;
            targetRawDragPoint = peelGrabPoint;
            previousTargetRawDragPoint = peelGrabPoint;
            rawDragVelocity = Vector2.zero;
            isResistanceBypassed = true;
            Opacity = 1f;

            automaticPeelStartPoint = peelGrabPoint;
            automaticPeelTargetPoint = peelTargetDragPoint;
            automaticPeelElapsedTime = 0f;
            activeAutomaticPeelDuration = Mathf.Max(duration, 0.01f);
            automaticPeelEndsWithPeelOff = peelOffWhenDone;
            automaticPeelCompletedCallback = onCompleted;
            State = DoodleStickerPeelState.AutomaticallyPeeling;
            onPeelStarted.Invoke();
        }

        private void TickAutomaticPeel(float deltaTime)
        {
            automaticPeelElapsedTime += deltaTime;
            float progress = Mathf.Clamp01(automaticPeelElapsedTime / activeAutomaticPeelDuration);
            float travelledFraction = progress >= 1f ? 1f : automaticPeelEasing.Evaluate(progress);
            rawDragPoint = Vector2.LerpUnclamped(automaticPeelStartPoint, automaticPeelTargetPoint, travelledFraction);
            targetRawDragPoint = rawDragPoint;

            if (progress < 1f)
            {
                return;
            }

            Action completedCallback = automaticPeelCompletedCallback;
            automaticPeelCompletedCallback = null;

            if (automaticPeelEndsWithPeelOff)
            {
                BeginPeelOff();
            }
            else
            {
                State = DoodleStickerPeelState.Resting;
            }

            onAutomaticPeelCompleted.Invoke();
            completedCallback?.Invoke();
        }

        private void CompleteSmoothFlatten()
        {
            Action completedCallback = flattenCompletedCallback;
            flattenCompletedCallback = null;
            onFlattened.Invoke();
            completedCallback?.Invoke();

            if (hasPendingAutomaticPeel && State == DoodleStickerPeelState.Resting)
            {
                hasPendingAutomaticPeel = false;
                Action pendingCallback = pendingAutomaticPeelCallback;
                pendingAutomaticPeelCallback = null;
                StartAutomaticPeelNow(pendingAutomaticPeelGrabPoint, pendingAutomaticPeelTargetPoint, pendingAutomaticPeelDuration, pendingAutomaticPeelEndsWithPeelOff, pendingCallback);
            }
        }

        private void ClearAutomaticAnimationRequests()
        {
            hasPendingAutomaticPeel = false;
            pendingAutomaticPeelCallback = null;
            automaticPeelCompletedCallback = null;
            flattenCompletedCallback = null;
        }

        public DoodleStickerFold CreateFold() => DoodleStickerFold.Create(grabPoint, DragPoint, curlSpaceScale, maximumCurlRadius);

        public void ResetToInitialPose()
        {
            ClearAutomaticAnimationRequests();
            activePointerId = int.MinValue;
            isContinuingExistingPeel = false;
            isResistanceBypassed = true;
            grabPoint = initialGrabPoint;
            rawDragPoint = initialDragPoint;
            targetRawDragPoint = initialDragPoint;
            previousTargetRawDragPoint = initialDragPoint;
            rawDragVelocity = Vector2.zero;
            pointerVelocity = Vector2.zero;
            Opacity = 1f;
            State = DoodleStickerPeelState.Resting;
        }

        public void Flatten()
        {
            ClearAutomaticAnimationRequests();
            SnapFlat();
        }

        private void SnapFlat()
        {
            activePointerId = int.MinValue;
            isContinuingExistingPeel = false;
            isResistanceBypassed = false;
            rawDragPoint = grabPoint;
            targetRawDragPoint = grabPoint;
            previousTargetRawDragPoint = grabPoint;
            rawDragVelocity = Vector2.zero;
            pointerVelocity = Vector2.zero;
            Opacity = 1f;
            State = DoodleStickerPeelState.Resting;
        }

        public bool TryBeginPointer(int pointerId, Vector2 stickerPoint, DoodleStickerSurfaceRegion touchedRegion)
        {
            if (activePointerId != int.MinValue || touchedRegion == DoodleStickerSurfaceRegion.None
                || State == DoodleStickerPeelState.PeelingOff || State == DoodleStickerPeelState.PeeledOff)
            {
                return false;
            }

            ClearAutomaticAnimationRequests();
            activePointerId = pointerId;
            pointerVelocity = Vector2.zero;
            bool startsNewPeel = touchedRegion == DoodleStickerSurfaceRegion.Stuck || IsFlat;

            if (startsNewPeel)
            {
                grabPoint = stickerPoint;
                rawDragPoint = stickerPoint;
                rawDragVelocity = Vector2.zero;
                isResistanceBypassed = false;
                isContinuingExistingPeel = false;
            }
            else
            {
                if (isResistanceBypassed)
                {
                    rawDragPoint = RemovePeelResistance(rawDragPoint);
                    isResistanceBypassed = false;
                }
                pointerAnchorPoint = stickerPoint;
                rawDragAnchorPoint = rawDragPoint;
                isContinuingExistingPeel = true;
            }

            targetRawDragPoint = rawDragPoint;
            previousTargetRawDragPoint = rawDragPoint;
            State = DoodleStickerPeelState.Dragging;

            if (startsNewPeel)
            {
                onPeelStarted.Invoke();
            }
            return true;
        }

        public void MovePointer(int pointerId, Vector2 stickerPoint)
        {
            if (pointerId != activePointerId || State != DoodleStickerPeelState.Dragging)
            {
                return;
            }

            targetRawDragPoint = isContinuingExistingPeel
                ? rawDragAnchorPoint + (stickerPoint - pointerAnchorPoint)
                : stickerPoint;
        }

        public bool ReleasePointer(int pointerId)
        {
            if (pointerId != activePointerId)
            {
                return false;
            }

            activePointerId = int.MinValue;
            if (State != DoodleStickerPeelState.Dragging)
            {
                return false;
            }

            BeginRelease();
            onPeelReleased.Invoke();
            return true;
        }

        public void CancelPointer()
        {
            if (activePointerId != int.MinValue)
            {
                ReleasePointer(activePointerId);
            }
        }

        public bool Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return false;
            }

            switch (State)
            {
                case DoodleStickerPeelState.Dragging:
                    TickDragging(deltaTime);
                    return true;
                case DoodleStickerPeelState.Settling:
                    TickSettling(deltaTime);
                    return true;
                case DoodleStickerPeelState.SpringingBack:
                    TickSpringingBack(deltaTime);
                    return true;
                case DoodleStickerPeelState.Flinging:
                    TickFlinging(deltaTime);
                    return true;
                case DoodleStickerPeelState.PeelingOff:
                    TickPeelingOff(deltaTime);
                    return true;
                case DoodleStickerPeelState.AutomaticallyPeeling:
                    TickAutomaticPeel(deltaTime);
                    return true;
                default:
                    return false;
            }
        }

        private void TickDragging(float deltaTime)
        {
            Vector2 instantaneousPointerVelocity = (targetRawDragPoint - previousTargetRawDragPoint) / deltaTime;
            previousTargetRawDragPoint = targetRawDragPoint;
            float velocityBlend = 1f - Mathf.Exp(-deltaTime / PointerVelocityResponseTime);
            pointerVelocity = Vector2.Lerp(pointerVelocity, instantaneousPointerVelocity, velocityBlend);

            MoveRawDragPointTowards(targetRawDragPoint, dragSmoothingTime, deltaTime);
        }

        private void TickSettling(float deltaTime)
        {
            MoveRawDragPointTowards(targetRawDragPoint, dragSmoothingTime, deltaTime);
            if (CurlSpaceDistance(rawDragPoint, targetRawDragPoint) < SettleDistanceThreshold)
            {
                rawDragPoint = targetRawDragPoint;
                rawDragVelocity = Vector2.zero;
                State = DoodleStickerPeelState.Resting;
            }
        }

        private void TickSpringingBack(float deltaTime)
        {
            flattenElapsedTime += deltaTime;
            float flattenProgress = Mathf.Clamp01(flattenElapsedTime / activeFlattenDuration);
            if (flattenProgress >= 1f)
            {
                SnapFlat();
                CompleteSmoothFlatten();
                return;
            }

            float remainingFraction = 1f - Mathf.Clamp01(flattenEasing.Evaluate(flattenProgress));
            float peelDistance = flattenStartPeelDistance * remainingFraction;
            float foldPosition = Mathf.Lerp(flattenEndFoldPosition, flattenStartFoldPosition, remainingFraction);
            float curlRadius = Mathf.Max(Mathf.Min(maximumCurlRadius, peelDistance / Mathf.PI), DoodleStickerFold.MinimumPeelDistance);
            float grabPosition = foldPosition + (peelDistance + Mathf.PI * curlRadius) * 0.5f;

            Vector2 lateralAxis = new Vector2(-flattenAxis.y, flattenAxis.x);
            Vector2 grabCurlPoint = flattenAxis * grabPosition + lateralAxis * flattenLateralOffset;
            Vector2 dragCurlPoint = grabCurlPoint - flattenAxis * peelDistance;
            grabPoint = CurlToStickerSpace(grabCurlPoint);
            rawDragPoint = CurlToStickerSpace(dragCurlPoint);
            targetRawDragPoint = rawDragPoint;
        }

        private void BeginSmoothFlatten(float durationOverride = -1f)
        {
            activeFlattenDuration = durationOverride > 0f ? durationOverride : flattenDuration;
            DoodleStickerFold fold = CreateFold();
            if (fold.IsFlat)
            {
                SnapFlat();
                CompleteSmoothFlatten();
                return;
            }

            float farthestSheetPosition = float.MinValue;
            if (sheetExtentPoints.Length > 0)
            {
                for (int pointIndex = 0; pointIndex < sheetExtentPoints.Length; pointIndex++)
                {
                    farthestSheetPosition = Mathf.Max(farthestSheetPosition, Vector2.Dot(Vector2.Scale(sheetExtentPoints[pointIndex], curlSpaceScale), fold.Axis));
                }
            }
            else
            {
                for (int cornerIndex = 0; cornerIndex < 4; cornerIndex++)
                {
                    Vector2 corner = new Vector2(cornerIndex & 1, cornerIndex >> 1);
                    farthestSheetPosition = Mathf.Max(farthestSheetPosition, Vector2.Dot(Vector2.Scale(corner, curlSpaceScale), fold.Axis));
                }
            }

            Vector2 grabCurlPoint = Vector2.Scale(grabPoint, curlSpaceScale);
            Vector2 lateralAxis = new Vector2(-fold.Axis.y, fold.Axis.x);
            flattenAxis = fold.Axis;
            flattenLateralOffset = Vector2.Dot(grabCurlPoint, lateralAxis);
            flattenStartPeelDistance = CurlSpaceDistance(grabPoint, DragPoint);
            flattenStartFoldPosition = Vector2.Dot(fold.Origin, fold.Axis);
            flattenEndFoldPosition = Mathf.Max(farthestSheetPosition + FlattenOvershootDistance, flattenStartFoldPosition);
            flattenElapsedTime = 0f;

            rawDragPoint = DragPoint;
            rawDragVelocity = Vector2.zero;
            isResistanceBypassed = true;
            State = DoodleStickerPeelState.SpringingBack;
        }

        private Vector2 CurlToStickerSpace(Vector2 curlPoint) => new Vector2(curlPoint.x / curlSpaceScale.x, curlPoint.y / curlSpaceScale.y);

        private void TickFlinging(float deltaTime)
        {
            rawDragPoint += rawDragVelocity * deltaTime;
            rawDragVelocity *= Mathf.Exp(-flingDeceleration * deltaTime);

            if (ShouldPeelOff())
            {
                BeginPeelOff();
                return;
            }

            if (Vector2.Dot(Vector2.Scale(rawDragPoint - grabPoint, curlSpaceScale), releaseDirection) <= 0f)
            {
                rawDragPoint = grabPoint + CurlToStickerSpace(releaseDirection * 1e-3f);
                BeginSmoothFlatten();
                return;
            }

            if (CurlSpaceLength(rawDragVelocity) < FlingStopSpeed)
            {
                rawDragVelocity = Vector2.zero;
                targetRawDragPoint = rawDragPoint;
                State = DoodleStickerPeelState.Resting;
            }
        }

        private void TickPeelingOff(float deltaTime)
        {
            float travelDistance = peelOffSpeed * deltaTime;
            rawDragPoint += new Vector2(peelOffDirection.x / curlSpaceScale.x, peelOffDirection.y / curlSpaceScale.y) * travelDistance;

            DoodleStickerFold fold = CreateFold();
            if (fold.EvaluateLiftedFraction() < 1f || !HasOvershotStickerRect(fold))
            {
                return;
            }

            Opacity = peelOffFadeDuration > 0f ? Mathf.Max(0f, Opacity - deltaTime / peelOffFadeDuration) : 0f;
            if (Opacity <= 0f)
            {
                State = DoodleStickerPeelState.PeeledOff;
                onPeeledOff.Invoke();
            }
        }

        private void BeginRelease()
        {
            isContinuingExistingPeel = false;
            Vector2 releaseOffset = Vector2.Scale(rawDragPoint - grabPoint, curlSpaceScale);
            releaseDirection = releaseOffset.sqrMagnitude > 0f ? releaseOffset.normalized : Vector2.zero;

            if (IsFlat)
            {
                SnapFlat();
                return;
            }

            if (ShouldPeelOff())
            {
                BeginPeelOff();
                return;
            }

            switch (releaseBehaviour)
            {
                case DoodleStickerReleaseBehaviour.SpringBack:
                    BeginSmoothFlatten();
                    break;
                case DoodleStickerReleaseBehaviour.Fling:
                    rawDragVelocity = pointerVelocity * flingVelocityScale;
                    State = DoodleStickerPeelState.Flinging;
                    break;
                default:
                    State = DoodleStickerPeelState.Settling;
                    break;
            }
        }

        private bool ShouldPeelOff() => peelOffThreshold > 0f && LiftedFraction >= peelOffThreshold;

        private void BeginPeelOff()
        {
            rawDragPoint = DragPoint;
            isResistanceBypassed = true;
            rawDragVelocity = Vector2.zero;
            Vector2 peelOffsetInCurlSpace = Vector2.Scale(rawDragPoint - grabPoint, curlSpaceScale);
            peelOffDirection = peelOffsetInCurlSpace.sqrMagnitude > 0f ? peelOffsetInCurlSpace.normalized : Vector2.left;
            State = DoodleStickerPeelState.PeelingOff;
        }

        private bool HasOvershotStickerRect(DoodleStickerFold fold)
        {
            float nearestCornerDistance = float.MaxValue;
            for (int cornerIndex = 0; cornerIndex < 4; cornerIndex++)
            {
                Vector2 corner = new Vector2(cornerIndex & 1, cornerIndex >> 1);
                nearestCornerDistance = Mathf.Min(nearestCornerDistance, fold.DistanceFromFold(fold.StickerToCurlSpace(corner)));
            }
            return nearestCornerDistance >= PeelOffOvershootFraction * Mathf.Max(fold.Radius, 0.1f);
        }

        private void MoveRawDragPointTowards(Vector2 destination, float smoothingTime, float deltaTime)
        {
            rawDragPoint = smoothingTime > 0f
                ? Vector2.SmoothDamp(rawDragPoint, destination, ref rawDragVelocity, smoothingTime, Mathf.Infinity, deltaTime)
                : destination;
        }

        private Vector2 ApplyPeelResistance(Vector2 unresistedDragPoint)
        {
            Vector2 peelOffset = Vector2.Scale(unresistedDragPoint - grabPoint, curlSpaceScale);
            float peelDistance = peelOffset.magnitude;
            if (peelDistance <= 0f)
            {
                return unresistedDragPoint;
            }

            float resistedDistance = ResistPeelDistance(peelDistance);
            Vector2 resistedOffset = peelOffset * (resistedDistance / peelDistance);
            return grabPoint + new Vector2(resistedOffset.x / curlSpaceScale.x, resistedOffset.y / curlSpaceScale.y);
        }

        private Vector2 RemovePeelResistance(Vector2 resistedDragPoint)
        {
            Vector2 peelOffset = Vector2.Scale(resistedDragPoint - grabPoint, curlSpaceScale);
            float resistedDistance = peelOffset.magnitude;
            if (resistedDistance <= 0f)
            {
                return resistedDragPoint;
            }

            float unresistedDistance = UnresistPeelDistance(resistedDistance);
            Vector2 unresistedOffset = peelOffset * (unresistedDistance / resistedDistance);
            return grabPoint + new Vector2(unresistedOffset.x / curlSpaceScale.x, unresistedOffset.y / curlSpaceScale.y);
        }

        private float ResistPeelDistance(float peelDistance)
        {
            float easedDistance = peelDistance * (1f - peelResistance);
            if (maximumPeelDistance <= 0f)
            {
                return easedDistance;
            }
            return maximumPeelDistance * (1f - Mathf.Exp(-easedDistance / maximumPeelDistance));
        }

        private float UnresistPeelDistance(float resistedDistance)
        {
            float easedDistance = resistedDistance;
            if (maximumPeelDistance > 0f)
            {
                float limitedFraction = Mathf.Min(resistedDistance / maximumPeelDistance, 0.999f);
                easedDistance = -maximumPeelDistance * Mathf.Log(1f - limitedFraction);
            }
            return easedDistance / Mathf.Max(1f - peelResistance, 1e-3f);
        }

        private float CurlSpaceLength(Vector2 stickerVector) => Vector2.Scale(stickerVector, curlSpaceScale).magnitude;

        private float CurlSpaceDistance(Vector2 firstPoint, Vector2 secondPoint) => CurlSpaceLength(firstPoint - secondPoint);
    }
}
