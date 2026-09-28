using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace DoodleStickers.EditorTooling
{
    internal static class DoodleStickerEditorUtility
    {
        private sealed class AtlasCheckResult
        {
            public double CheckedAtTime;
            public readonly List<DoodleStickerIssue> Issues = new List<DoodleStickerIssue>();
        }

        private const double AtlasCheckLifetimeSeconds = 2.0;
        private const float HandleSizeScale = 0.08f;
        private const float FoldLineHalfLength = 0.75f;

        private static readonly Dictionary<Sprite, AtlasCheckResult> AtlasCheckCache = new Dictionary<Sprite, AtlasCheckResult>();
        private static readonly List<DoodleStickerIssue> IssueBuffer = new List<DoodleStickerIssue>();
        private static readonly Color StickerOutlineColor = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color FoldLineColor = new Color(1f, 0.55f, 0.1f, 0.95f);
        private static readonly Color CurlTopLineColor = new Color(1f, 0.55f, 0.1f, 0.45f);
        private static readonly Color GrabHandleColor = new Color(0.25f, 0.8f, 1f, 1f);
        private static readonly Color DragHandleColor = new Color(1f, 0.35f, 0.35f, 1f);

        public static void DrawIssues(Action<List<DoodleStickerIssue>> collectIssues, Sprite sprite)
        {
            IssueBuffer.Clear();
            collectIssues(IssueBuffer);
            CollectAtlasIssues(sprite, IssueBuffer);

            for (int issueIndex = 0; issueIndex < IssueBuffer.Count; issueIndex++)
            {
                DoodleStickerIssue issue = IssueBuffer[issueIndex];
                MessageType messageType = issue.Severity == DoodleStickerIssueSeverity.Error ? MessageType.Error
                    : issue.Severity == DoodleStickerIssueSeverity.Warning ? MessageType.Warning
                    : MessageType.Info;
                EditorGUILayout.HelpBox(issue.Message, messageType);
            }
        }

        public static void DrawPeelSettings(SerializedProperty peelProperty, bool isPlaying, DoodleStickerPeel livePeel, Action flattenPose, Action resetPose, Action peelAutomatically, Action flattenAutomatically)
        {
            DrawSectionHeader("Realistic Peeling");
            SerializedProperty requireEdgeGrabProperty = DrawChild(peelProperty, "requireEdgeGrab");
            if (requireEdgeGrabProperty.boolValue)
            {
                EditorGUI.indentLevel++;
                DrawChild(peelProperty, "edgeGrabDistance");
                EditorGUI.indentLevel--;
            }
            SerializedProperty restrictDirectionProperty = DrawChild(peelProperty, "restrictPeelDirection");
            if (restrictDirectionProperty.boolValue)
            {
                EditorGUI.indentLevel++;
                DrawChild(peelProperty, "maximumPeelAngle");
                EditorGUI.indentLevel--;
            }

            DrawSectionHeader("Release");
            DrawChild(peelProperty, "releaseBehaviour");
            DrawChild(peelProperty, "dragSmoothingTime");

            DoodleStickerReleaseBehaviour releaseBehaviour = (DoodleStickerReleaseBehaviour)peelProperty.FindPropertyRelative("releaseBehaviour").enumValueIndex;
            if (releaseBehaviour == DoodleStickerReleaseBehaviour.Fling)
            {
                EditorGUI.indentLevel++;
                DrawChild(peelProperty, "flingDeceleration");
                DrawChild(peelProperty, "flingVelocityScale");
                EditorGUI.indentLevel--;
            }

            DrawSectionHeader("Roll Back Flat");
            DrawChild(peelProperty, "flattenDuration");
            DrawChild(peelProperty, "flattenEasing");

            DrawSectionHeader("Automatic Peel And Flatten");
            DrawChild(peelProperty, "automaticPeelAmount");
            DrawChild(peelProperty, "automaticPeelDuration");
            DrawChild(peelProperty, "automaticPeelEasing");
            DrawChild(peelProperty, "automaticFlattenStartAmount");
            DrawChild(peelProperty, "automaticPeelAngleVariation");
            DrawChild(peelProperty, "automaticEdgeMargin");

            DrawSectionHeader("Glue");
            DrawChild(peelProperty, "peelResistance");
            DrawChild(peelProperty, "maximumPeelDistance");

            DrawSectionHeader("Peel Off");
            SerializedProperty peelOffThresholdProperty = DrawChild(peelProperty, "peelOffThreshold");
            if (peelOffThresholdProperty.floatValue > 0f)
            {
                EditorGUI.indentLevel++;
                DrawChild(peelProperty, "peelOffSpeed");
                DrawChild(peelProperty, "peelOffFadeDuration");
                EditorGUI.indentLevel--;
            }

            DrawSectionHeader("Initial Pose");
            SerializedProperty initialGrabProperty = DrawChild(peelProperty, "initialGrabPoint");
            SerializedProperty initialDragProperty = DrawChild(peelProperty, "initialDragPoint");
            EditorGUILayout.HelpBox("Drag the blue (grab) and red (drag) handles in the Scene view to pose the curl without entering Play mode.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Flat"))
                {
                    initialGrabProperty.vector2Value = Vector2.zero;
                    initialDragProperty.vector2Value = Vector2.zero;
                }
                if (GUILayout.Button("Corner Peel"))
                {
                    initialGrabProperty.vector2Value = new Vector2(1f, 0f);
                    initialDragProperty.vector2Value = new Vector2(0.62f, 0.38f);
                }
                if (GUILayout.Button("Edge Peel"))
                {
                    initialGrabProperty.vector2Value = new Vector2(1f, 0.5f);
                    initialDragProperty.vector2Value = new Vector2(0.7f, 0.5f);
                }
            }

            if (isPlaying && livePeel != null)
            {
                DrawSectionHeader("Live State");
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.EnumPopup("State", livePeel.State);
                    EditorGUILayout.Slider("Lifted Fraction", livePeel.LiftedFraction, 0f, 1f);
                    EditorGUILayout.Slider("Opacity", livePeel.Opacity, 0f, 1f);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Flatten")) flattenPose();
                    if (GUILayout.Button("Reset To Initial Pose")) resetPose();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Peel Automatically")) peelAutomatically();
                    if (GUILayout.Button("Flatten Automatically")) flattenAutomatically();
                }
            }

            DrawSectionHeader("Events");
            DrawChild(peelProperty, "onPeelStarted");
            DrawChild(peelProperty, "onPeelReleased");
            DrawChild(peelProperty, "onPeeledOff");
            DrawChild(peelProperty, "onAutomaticPeelCompleted");
            DrawChild(peelProperty, "onFlattened");
        }

        public static void DrawSortingLayerPopup(SerializedProperty sortingLayerIdProperty)
        {
            SortingLayer[] sortingLayers = SortingLayer.layers;
            string[] layerNames = new string[sortingLayers.Length];
            int selectedIndex = 0;
            for (int layerIndex = 0; layerIndex < sortingLayers.Length; layerIndex++)
            {
                layerNames[layerIndex] = sortingLayers[layerIndex].name;
                if (sortingLayers[layerIndex].id == sortingLayerIdProperty.intValue)
                {
                    selectedIndex = layerIndex;
                }
            }

            EditorGUI.showMixedValue = sortingLayerIdProperty.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(new GUIContent("Sorting Layer", "Sorting layer the sticker renders and receives pointer input in."), selectedIndex, layerNames);
            if (EditorGUI.EndChangeCheck() && newIndex >= 0 && newIndex < sortingLayers.Length)
            {
                sortingLayerIdProperty.intValue = sortingLayers[newIndex].id;
            }
            EditorGUI.showMixedValue = false;
        }

        public static void DrawSceneCurl(
            SerializedObject serializedObject,
            DoodleStickerFold currentFold,
            Func<Vector2, Vector3> stickerPointToWorld,
            Func<Vector3, Vector2> worldToStickerPoint,
            bool canEditPose)
        {
            Vector3 bottomLeft = stickerPointToWorld(new Vector2(0f, 0f));
            Vector3 topLeft = stickerPointToWorld(new Vector2(0f, 1f));
            Vector3 topRight = stickerPointToWorld(new Vector2(1f, 1f));
            Vector3 bottomRight = stickerPointToWorld(new Vector2(1f, 0f));

            Handles.color = StickerOutlineColor;
            Handles.DrawPolyLine(bottomLeft, topLeft, topRight, bottomRight, bottomLeft);

            if (!currentFold.IsFlat)
            {
                DrawFoldLine(currentFold, currentFold.Origin, FoldLineColor, 2f, stickerPointToWorld);
                DrawFoldLine(currentFold, currentFold.Origin + currentFold.Axis * currentFold.Radius, CurlTopLineColor, 1f, stickerPointToWorld);
            }

            if (!canEditPose)
            {
                return;
            }

            serializedObject.Update();
            SerializedProperty peelProperty = serializedObject.FindProperty("peel");
            SerializedProperty initialGrabProperty = peelProperty.FindPropertyRelative("initialGrabPoint");
            SerializedProperty initialDragProperty = peelProperty.FindPropertyRelative("initialDragPoint");

            bool isPosed = initialGrabProperty.vector2Value != initialDragProperty.vector2Value;
            Vector2 grabPoint = isPosed ? initialGrabProperty.vector2Value : new Vector2(1f, 0f);
            Vector2 dragPoint = isPosed ? initialDragProperty.vector2Value : new Vector2(1f, 0f);

            EditorGUI.BeginChangeCheck();
            Vector3 grabWorldPoint = DrawPointHandle(stickerPointToWorld(grabPoint), GrabHandleColor, Handles.CircleHandleCap, 1f);
            Vector3 dragWorldPoint = DrawPointHandle(stickerPointToWorld(dragPoint), DragHandleColor, Handles.DotHandleCap, 0.6f);
            if (EditorGUI.EndChangeCheck())
            {
                initialGrabProperty.vector2Value = worldToStickerPoint(grabWorldPoint);
                initialDragProperty.vector2Value = worldToStickerPoint(dragWorldPoint);
                serializedObject.ApplyModifiedProperties();
            }

            if (isPosed)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.5f);
                Handles.DrawDottedLine(stickerPointToWorld(grabPoint), stickerPointToWorld(dragPoint), 4f);
            }
        }

        private static Vector3 DrawPointHandle(Vector3 worldPoint, Color handleColor, Handles.CapFunction capFunction, float sizeMultiplier)
        {
            Handles.color = handleColor;
            float handleSize = HandleUtility.GetHandleSize(worldPoint) * HandleSizeScale * sizeMultiplier;
#if UNITY_2022_1_OR_NEWER
            return Handles.FreeMoveHandle(worldPoint, handleSize, Vector3.zero, capFunction);
#else
            return Handles.FreeMoveHandle(worldPoint, Quaternion.identity, handleSize, Vector3.zero, capFunction);
#endif
        }

        private static void DrawFoldLine(DoodleStickerFold fold, Vector2 curlPointOnLine, Color lineColor, float thickness, Func<Vector2, Vector3> stickerPointToWorld)
        {
            Vector2 lateralAxis = new Vector2(-fold.Axis.y, fold.Axis.x);
            Vector2 stickerCenterInCurlSpace = fold.StickerToCurlSpace(new Vector2(0.5f, 0.5f));
            float centerLateral = Vector2.Dot(stickerCenterInCurlSpace - curlPointOnLine, lateralAxis);
            Vector2 lineCenter = curlPointOnLine + lateralAxis * centerLateral;

            Vector3 lineStart = stickerPointToWorld(fold.CurlToStickerSpace(lineCenter - lateralAxis * FoldLineHalfLength));
            Vector3 lineEnd = stickerPointToWorld(fold.CurlToStickerSpace(lineCenter + lateralAxis * FoldLineHalfLength));
            Handles.color = lineColor;
#if UNITY_2020_2_OR_NEWER
            Handles.DrawLine(lineStart, lineEnd, thickness);
#else
            Handles.DrawLine(lineStart, lineEnd);
#endif
        }

        private static SerializedProperty DrawChild(SerializedProperty parentProperty, string childName)
        {
            SerializedProperty childProperty = parentProperty.FindPropertyRelative(childName);
            EditorGUILayout.PropertyField(childProperty, true);
            return childProperty;
        }

        public static void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void CollectAtlasIssues(Sprite sprite, List<DoodleStickerIssue> issues)
        {
            if (sprite == null)
            {
                return;
            }

            double currentTime = EditorApplication.timeSinceStartup;
            if (!AtlasCheckCache.TryGetValue(sprite, out AtlasCheckResult checkResult) || currentTime - checkResult.CheckedAtTime > AtlasCheckLifetimeSeconds)
            {
                checkResult = new AtlasCheckResult { CheckedAtTime = currentTime };
                CheckSpriteAtlases(sprite, checkResult.Issues);
                AtlasCheckCache[sprite] = checkResult;
            }

            issues.AddRange(checkResult.Issues);
        }

        private static void CheckSpriteAtlases(Sprite sprite, List<DoodleStickerIssue> issues)
        {
            string[] atlasGuids = AssetDatabase.FindAssets("t:SpriteAtlas");
            for (int atlasIndex = 0; atlasIndex < atlasGuids.Length; atlasIndex++)
            {
                SpriteAtlas spriteAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AssetDatabase.GUIDToAssetPath(atlasGuids[atlasIndex]));
                if (spriteAtlas == null || !spriteAtlas.CanBindTo(sprite))
                {
                    continue;
                }

                SpriteAtlasPackingSettings packingSettings = spriteAtlas.GetPackingSettings();
                if (packingSettings.enableTightPacking)
                {
                    issues.Add(new DoodleStickerIssue(DoodleStickerIssueSeverity.Warning,
                        "Sprite Atlas \"" + spriteAtlas.name + "\" uses Tight Packing, so a curl can reveal neighbouring sprites. Turn it off for this atlas."));
                }
                if (packingSettings.enableRotation)
                {
                    issues.Add(new DoodleStickerIssue(DoodleStickerIssueSeverity.Error,
                        "Sprite Atlas \"" + spriteAtlas.name + "\" allows rotation, which the sticker cannot sample. Turn off Allow Rotation for this atlas."));
                }
            }
        }
    }
}
