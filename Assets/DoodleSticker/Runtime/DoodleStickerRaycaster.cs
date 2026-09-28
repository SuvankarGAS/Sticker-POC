using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DoodleStickers
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Doodle Stickers/Doodle Sticker Raycaster")]
    public sealed class DoodleStickerRaycaster : BaseRaycaster
    {
        [Tooltip("Layers whose stickers this raycaster can hit.")]
        [SerializeField] private LayerMask eventMask = ~0;

        private Camera raycastCamera;

        public override Camera eventCamera
        {
            get
            {
                if (raycastCamera == null)
                {
                    raycastCamera = GetComponent<Camera>();
                }
                return raycastCamera;
            }
        }

        public LayerMask EventMask
        {
            get => eventMask;
            set => eventMask = value;
        }

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            Camera camera = eventCamera;
            if (camera == null || !camera.pixelRect.Contains(eventData.position))
            {
                return;
            }

            Ray pointerRay = camera.ScreenPointToRay(eventData.position);
            float maximumDistance = camera.farClipPlane;
            int hittableLayers = camera.cullingMask & eventMask;
            IReadOnlyList<DoodleSticker> activeStickers = DoodleSticker.ActiveStickers;

            for (int stickerIndex = 0; stickerIndex < activeStickers.Count; stickerIndex++)
            {
                DoodleSticker sticker = activeStickers[stickerIndex];
                if ((hittableLayers & (1 << sticker.gameObject.layer)) == 0)
                {
                    continue;
                }

                if (!sticker.TryRaycast(pointerRay, out float hitDistance, out Vector3 hitPoint) || hitDistance > maximumDistance)
                {
                    continue;
                }

                resultAppendList.Add(new RaycastResult
                {
                    gameObject = sticker.gameObject,
                    module = this,
                    distance = hitDistance,
                    worldPosition = hitPoint,
                    worldNormal = -sticker.transform.forward,
                    screenPosition = eventData.position,
                    index = resultAppendList.Count,
                    sortingLayer = sticker.SortingLayerId,
                    sortingOrder = sticker.SortingOrder
                });
            }
        }
    }
}
