using System.Collections.Generic;
using UnityEngine;

namespace DoodleStickers
{
    public enum DoodleStickerIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct DoodleStickerIssue
    {
        public readonly DoodleStickerIssueSeverity Severity;
        public readonly string Message;

        public DoodleStickerIssue(DoodleStickerIssueSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }
    }

    public static class DoodleStickerValidation
    {
        public static void CollectSpriteIssues(Sprite sprite, List<DoodleStickerIssue> issues)
        {
            if (sprite == null)
            {
                issues.Add(new DoodleStickerIssue(DoodleStickerIssueSeverity.Error, "Assign a sprite."));
                return;
            }

            if (sprite.packed && sprite.packingMode == SpritePackingMode.Tight)
            {
                issues.Add(new DoodleStickerIssue(DoodleStickerIssueSeverity.Warning,
                    "The sprite is tightly packed in its atlas, so a curl can reveal neighbouring sprites. Turn off Tight Packing in the Sprite Atlas."));
            }

            if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None)
            {
                issues.Add(new DoodleStickerIssue(DoodleStickerIssueSeverity.Error,
                    "The sprite is rotated in its atlas, which the sticker cannot sample. Turn off Allow Rotation in the Sprite Atlas."));
            }

            if (sprite.border != Vector4.zero)
            {
                issues.Add(new DoodleStickerIssue(DoodleStickerIssueSeverity.Info,
                    "The sprite has 9-slice borders. Stickers always draw as a single simple sprite, so the borders are ignored."));
            }
        }

        public static void CollectMaterialIssues(Material material, string expectedShaderName, List<DoodleStickerIssue> issues)
        {
            if (material == null)
            {
                issues.Add(new DoodleStickerIssue(DoodleStickerIssueSeverity.Error,
                    "Assign a material that uses the \"" + expectedShaderName + "\" shader."));
                return;
            }

            if (material.shader == null || material.shader.name != expectedShaderName)
            {
                issues.Add(new DoodleStickerIssue(DoodleStickerIssueSeverity.Error,
                    "The material \"" + material.name + "\" must use the \"" + expectedShaderName + "\" shader."));
            }
        }

        public static void LogIssues(Object context, List<DoodleStickerIssue> issues)
        {
            for (int issueIndex = 0; issueIndex < issues.Count; issueIndex++)
            {
                DoodleStickerIssue issue = issues[issueIndex];
                string message = "[Doodle Sticker] " + context.name + ": " + issue.Message;
                switch (issue.Severity)
                {
                    case DoodleStickerIssueSeverity.Error:
                        Debug.LogError(message, context);
                        break;
                    case DoodleStickerIssueSeverity.Warning:
                        Debug.LogWarning(message, context);
                        break;
                }
            }
        }
    }
}
