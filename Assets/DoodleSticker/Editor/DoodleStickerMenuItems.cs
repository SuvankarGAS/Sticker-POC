using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleStickers.EditorTooling
{
    internal static class DoodleStickerMenuItems
    {
        [MenuItem("GameObject/2D Object/Doodle Sticker", false, 10)]
        private static void CreateDoodleSticker(MenuCommand menuCommand)
        {
            GameObject stickerObject = new GameObject("Doodle Sticker", typeof(MeshFilter), typeof(MeshRenderer), typeof(DoodleSticker));
            GameObjectUtility.SetParentAndAlign(stickerObject, menuCommand.context as GameObject);

            DoodleSticker sticker = stickerObject.GetComponent<DoodleSticker>();
            SerializedObject serializedSticker = new SerializedObject(sticker);
            serializedSticker.FindProperty("stickerMaterial").objectReferenceValue = FindMaterialUsingShader(DoodleStickerShaderIds.SpriteShaderName);
            serializedSticker.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(stickerObject, "Create Doodle Sticker");
            Selection.activeGameObject = stickerObject;
        }

        [MenuItem("GameObject/UI/Doodle Sticker", false, 2100)]
        private static void CreateDoodleStickerGraphic(MenuCommand menuCommand)
        {
            GameObject parentObject = menuCommand.context as GameObject;
            if (parentObject == null || parentObject.GetComponentInParent<Canvas>() == null)
            {
                Canvas existingCanvas = DoodleStickerObjectUtility.FindFirst<Canvas>();
                parentObject = existingCanvas != null ? existingCanvas.gameObject : null;
            }

            if (parentObject == null)
            {
                EditorApplication.ExecuteMenuItem("GameObject/UI/Canvas");
                parentObject = Selection.activeGameObject;
            }

            GameObject stickerObject = new GameObject("Doodle Sticker", typeof(RectTransform), typeof(CanvasRenderer), typeof(DoodleStickerGraphic));
            GameObjectUtility.SetParentAndAlign(stickerObject, parentObject);
            stickerObject.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 200f);

            DoodleStickerGraphic stickerGraphic = stickerObject.GetComponent<DoodleStickerGraphic>();
            stickerGraphic.material = FindMaterialUsingShader(DoodleStickerShaderIds.GraphicShaderName);

            Undo.RegisterCreatedObjectUndo(stickerObject, "Create Doodle Sticker (UI)");
            Selection.activeGameObject = stickerObject;
        }

        [MenuItem("Assets/Create/Doodle Stickers/Sticker Material", false, 300)]
        private static void CreateStickerMaterial()
        {
            CreateMaterialAsset(DoodleStickerShaderIds.SpriteShaderName, "Doodle Sticker.mat");
        }

        [MenuItem("Assets/Create/Doodle Stickers/UI Sticker Material", false, 301)]
        private static void CreateStickerGraphicMaterial()
        {
            CreateMaterialAsset(DoodleStickerShaderIds.GraphicShaderName, "Doodle Sticker UI.mat");
        }

        private static void CreateMaterialAsset(string shaderName, string fileName)
        {
            Shader stickerShader = Shader.Find(shaderName);
            if (stickerShader == null)
            {
                Debug.LogError("[Doodle Sticker] Shader \"" + shaderName + "\" was not found. Make sure the Doodle Sticker shaders are in the project.");
                return;
            }

            ProjectWindowUtil.CreateAsset(new Material(stickerShader), fileName);
        }

        private static Material FindMaterialUsingShader(string shaderName)
        {
            string[] materialGuids = AssetDatabase.FindAssets("t:Material");
            for (int materialIndex = 0; materialIndex < materialGuids.Length; materialIndex++)
            {
                Material candidate = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(materialGuids[materialIndex]));
                if (candidate != null && candidate.shader != null && candidate.shader.name == shaderName)
                {
                    return candidate;
                }
            }
            return null;
        }
    }
}
