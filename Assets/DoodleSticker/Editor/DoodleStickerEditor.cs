using UnityEditor;
using UnityEngine;

namespace DoodleStickers.EditorTooling
{
    [CustomEditor(typeof(DoodleSticker))]
    [CanEditMultipleObjects]
    public sealed class DoodleStickerEditor : Editor
    {
        private SerializedProperty spriteProperty;
        private SerializedProperty colorProperty;
        private SerializedProperty flipXProperty;
        private SerializedProperty flipYProperty;
        private SerializedProperty stickerMaterialProperty;
        private SerializedProperty sortingLayerIdProperty;
        private SerializedProperty sortingOrderProperty;
        private SerializedProperty interactableProperty;
        private SerializedProperty hitTestModeProperty;
        private SerializedProperty addRaycasterToMainCameraProperty;
        private SerializedProperty timeModeProperty;
        private SerializedProperty peeledOffActionProperty;
        private SerializedProperty peelProperty;

        private void OnEnable()
        {
            spriteProperty = serializedObject.FindProperty("sprite");
            colorProperty = serializedObject.FindProperty("color");
            flipXProperty = serializedObject.FindProperty("flipX");
            flipYProperty = serializedObject.FindProperty("flipY");
            stickerMaterialProperty = serializedObject.FindProperty("stickerMaterial");
            sortingLayerIdProperty = serializedObject.FindProperty("sortingLayerId");
            sortingOrderProperty = serializedObject.FindProperty("sortingOrder");
            interactableProperty = serializedObject.FindProperty("interactable");
            hitTestModeProperty = serializedObject.FindProperty("hitTestMode");
            addRaycasterToMainCameraProperty = serializedObject.FindProperty("addRaycasterToMainCamera");
            timeModeProperty = serializedObject.FindProperty("timeMode");
            peeledOffActionProperty = serializedObject.FindProperty("peeledOffAction");
            peelProperty = serializedObject.FindProperty("peel");
        }

        public override void OnInspectorGUI()
        {
            DoodleSticker sticker = (DoodleSticker)target;
            serializedObject.Update();

            if (!serializedObject.isEditingMultipleObjects)
            {
                DoodleStickerEditorUtility.DrawIssues(sticker.CollectIssues, sticker.Sprite);
            }

            DoodleStickerEditorUtility.DrawSectionHeader("Appearance");
            EditorGUILayout.PropertyField(spriteProperty);
            EditorGUILayout.PropertyField(colorProperty);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Flip");
                flipXProperty.boolValue = GUILayout.Toggle(flipXProperty.boolValue, "X", EditorStyles.miniButtonLeft);
                flipYProperty.boolValue = GUILayout.Toggle(flipYProperty.boolValue, "Y", EditorStyles.miniButtonRight);
            }
            EditorGUILayout.PropertyField(stickerMaterialProperty, new GUIContent("Material", "Template material using the \"Effects/Doodle Sticker\" shader. Stickers sharing it and a texture batch together."));
            DoodleStickerEditorUtility.DrawSortingLayerPopup(sortingLayerIdProperty);
            EditorGUILayout.PropertyField(sortingOrderProperty, new GUIContent("Order In Layer"));

            DoodleStickerEditorUtility.DrawSectionHeader("Interaction");
            EditorGUILayout.PropertyField(interactableProperty);
            EditorGUILayout.PropertyField(hitTestModeProperty);
            EditorGUILayout.PropertyField(addRaycasterToMainCameraProperty);
            EditorGUILayout.PropertyField(timeModeProperty);
            EditorGUILayout.PropertyField(peeledOffActionProperty);

            if (!serializedObject.isEditingMultipleObjects)
            {
                DoodleStickerEditorUtility.DrawPeelSettings(peelProperty, Application.isPlaying, sticker.Peel, sticker.Flatten, sticker.ResetToInitialPose, sticker.PeelAutomatically, sticker.FlattenAutomatically);
            }
            else
            {
                EditorGUILayout.PropertyField(peelProperty, true);
            }

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void OnSceneGUI()
        {
            DoodleSticker sticker = (DoodleSticker)target;
            if (!sticker.isActiveAndEnabled || !sticker.Shape.IsValid)
            {
                return;
            }

            DoodleStickerEditorUtility.DrawSceneCurl(
                serializedObject,
                sticker.CurrentFold,
                sticker.StickerPointToWorld,
                worldPoint => WorldToStickerPoint(sticker, worldPoint),
                !Application.isPlaying);
        }

        private static Vector2 WorldToStickerPoint(DoodleSticker sticker, Vector3 worldPoint)
        {
            Vector3 stickerNormal = sticker.transform.forward;
            Ray projectionRay = new Ray(worldPoint - stickerNormal, stickerNormal);
            return sticker.TryGetStickerPoint(projectionRay, out Vector2 stickerPoint, out _, out _) ? stickerPoint : Vector2.zero;
        }
    }
}
