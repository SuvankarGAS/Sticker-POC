using UnityEditor;
using UnityEngine;

namespace DoodleStickers.EditorTooling
{
    [CustomEditor(typeof(DoodleStickerGraphic))]
    [CanEditMultipleObjects]
    public sealed class DoodleStickerGraphicEditor : Editor
    {
        private SerializedProperty spriteProperty;
        private SerializedProperty colorProperty;
        private SerializedProperty materialProperty;
        private SerializedProperty preserveAspectProperty;
        private SerializedProperty raycastTargetProperty;
        private SerializedProperty maskableProperty;
        private SerializedProperty interactableProperty;
        private SerializedProperty hitTestModeProperty;
        private SerializedProperty timeModeProperty;
        private SerializedProperty peeledOffActionProperty;
        private SerializedProperty peelProperty;

        private void OnEnable()
        {
            spriteProperty = serializedObject.FindProperty("sprite");
            colorProperty = serializedObject.FindProperty("m_Color");
            materialProperty = serializedObject.FindProperty("m_Material");
            preserveAspectProperty = serializedObject.FindProperty("preserveAspect");
            raycastTargetProperty = serializedObject.FindProperty("m_RaycastTarget");
            maskableProperty = serializedObject.FindProperty("m_Maskable");
            interactableProperty = serializedObject.FindProperty("interactable");
            hitTestModeProperty = serializedObject.FindProperty("hitTestMode");
            timeModeProperty = serializedObject.FindProperty("timeMode");
            peeledOffActionProperty = serializedObject.FindProperty("peeledOffAction");
            peelProperty = serializedObject.FindProperty("peel");
        }

        public override void OnInspectorGUI()
        {
            DoodleStickerGraphic stickerGraphic = (DoodleStickerGraphic)target;
            serializedObject.Update();

            if (!serializedObject.isEditingMultipleObjects)
            {
                DoodleStickerEditorUtility.DrawIssues(stickerGraphic.CollectIssues, stickerGraphic.Sprite);
            }

            DoodleStickerEditorUtility.DrawSectionHeader("Appearance");
            EditorGUILayout.PropertyField(spriteProperty);
            EditorGUILayout.PropertyField(colorProperty);
            EditorGUILayout.PropertyField(materialProperty, new GUIContent("Material", "Material using the \"Effects/Doodle Sticker UI\" shader."));
            EditorGUILayout.PropertyField(preserveAspectProperty);
            EditorGUILayout.PropertyField(maskableProperty);
            if (GUILayout.Button("Set Native Size"))
            {
                foreach (Object selectedTarget in targets)
                {
                    DoodleStickerGraphic selectedGraphic = (DoodleStickerGraphic)selectedTarget;
                    Undo.RecordObject(selectedGraphic.rectTransform, "Set Native Size");
                    selectedGraphic.SetNativeSize();
                }
            }

            DoodleStickerEditorUtility.DrawSectionHeader("Interaction");
            EditorGUILayout.PropertyField(raycastTargetProperty);
            EditorGUILayout.PropertyField(interactableProperty);
            EditorGUILayout.PropertyField(hitTestModeProperty);
            EditorGUILayout.PropertyField(timeModeProperty);
            EditorGUILayout.PropertyField(peeledOffActionProperty);

            if (!serializedObject.isEditingMultipleObjects)
            {
                DoodleStickerEditorUtility.DrawPeelSettings(peelProperty, Application.isPlaying, stickerGraphic.Peel, stickerGraphic.Flatten, stickerGraphic.ResetToInitialPose, stickerGraphic.PeelAutomatically, stickerGraphic.FlattenAutomatically);
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
            DoodleStickerGraphic stickerGraphic = (DoodleStickerGraphic)target;
            if (!stickerGraphic.isActiveAndEnabled || stickerGraphic.Sprite == null)
            {
                return;
            }

            DoodleStickerEditorUtility.DrawSceneCurl(
                serializedObject,
                stickerGraphic.CurrentFold,
                stickerGraphic.StickerPointToWorld,
                worldPoint => WorldToStickerPoint(stickerGraphic, worldPoint),
                !Application.isPlaying);
        }

        private static Vector2 WorldToStickerPoint(DoodleStickerGraphic stickerGraphic, Vector3 worldPoint)
        {
            Vector2 localPoint = stickerGraphic.rectTransform.InverseTransformPoint(worldPoint);
            Rect drawRect = stickerGraphic.GetStickerDrawRect();
            if (drawRect.width <= 0f || drawRect.height <= 0f)
            {
                return Vector2.zero;
            }
            return new Vector2((localPoint.x - drawRect.xMin) / drawRect.width, (localPoint.y - drawRect.yMin) / drawRect.height);
        }
    }
}
