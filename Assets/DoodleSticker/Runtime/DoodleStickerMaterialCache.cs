using System.Collections.Generic;
using UnityEngine;

namespace DoodleStickers
{
    public static class DoodleStickerMaterialCache
    {
        private readonly struct MaterialKey : System.IEquatable<MaterialKey>
        {
            private readonly int templateId;
            private readonly int textureId;
            private readonly int alphaTextureId;

            public MaterialKey(Material template, Texture texture, Texture alphaTexture)
            {
                templateId = template.GetInstanceID();
                textureId = texture != null ? texture.GetInstanceID() : 0;
                alphaTextureId = alphaTexture != null ? alphaTexture.GetInstanceID() : 0;
            }

            public bool Equals(MaterialKey other) => templateId == other.templateId && textureId == other.textureId && alphaTextureId == other.alphaTextureId;
            public override bool Equals(object other) => other is MaterialKey otherKey && Equals(otherKey);
            public override int GetHashCode() => (templateId * 397 ^ textureId) * 397 ^ alphaTextureId;
        }

        private sealed class MaterialEntry
        {
            public MaterialKey Key;
            public Material Template;
            public Texture Texture;
            public Texture AlphaTexture;
            public Material SharedMaterial;
            public int ReferenceCount;
        }

        private static readonly Dictionary<MaterialKey, MaterialEntry> EntriesByKey = new Dictionary<MaterialKey, MaterialEntry>();
        private static readonly Dictionary<Material, MaterialEntry> EntriesByMaterial = new Dictionary<Material, MaterialEntry>();
        private static readonly List<MaterialEntry> EntryBuffer = new List<MaterialEntry>();

        public static Material Acquire(Material template, Texture texture, Texture alphaTexture)
        {
            if (template == null)
            {
                return null;
            }

            MaterialKey key = new MaterialKey(template, texture, alphaTexture);
            if (!EntriesByKey.TryGetValue(key, out MaterialEntry entry) || entry.SharedMaterial == null)
            {
                entry = new MaterialEntry
                {
                    Key = key,
                    Template = template,
                    Texture = texture,
                    AlphaTexture = alphaTexture,
                    SharedMaterial = new Material(template)
                    {
                        name = template.name + " (" + (texture != null ? texture.name : "No Texture") + ")",
                        hideFlags = HideFlags.HideAndDontSave
                    }
                };
                EntriesByKey[key] = entry;
                EntriesByMaterial[entry.SharedMaterial] = entry;
                SynchronizeWithTemplate(entry);
            }

            entry.ReferenceCount++;
            return entry.SharedMaterial;
        }

        public static void Release(Material sharedMaterial)
        {
            if (sharedMaterial == null || !EntriesByMaterial.TryGetValue(sharedMaterial, out MaterialEntry entry))
            {
                return;
            }

            entry.ReferenceCount--;
            if (entry.ReferenceCount > 0)
            {
                return;
            }

            EntriesByMaterial.Remove(sharedMaterial);
            EntriesByKey.Remove(entry.Key);
            DoodleStickerObjectUtility.DestroySafely(sharedMaterial);
        }

        public static void RefreshFromTemplates()
        {
            EntryBuffer.Clear();
            EntryBuffer.AddRange(EntriesByKey.Values);
            for (int entryIndex = 0; entryIndex < EntryBuffer.Count; entryIndex++)
            {
                MaterialEntry entry = EntryBuffer[entryIndex];
                if (entry.Template != null && entry.SharedMaterial != null)
                {
                    SynchronizeWithTemplate(entry);
                }
            }
            EntryBuffer.Clear();
        }

        private static void SynchronizeWithTemplate(MaterialEntry entry)
        {
            Material sharedMaterial = entry.SharedMaterial;
            if (sharedMaterial.shader != entry.Template.shader)
            {
                sharedMaterial.shader = entry.Template.shader;
            }

            sharedMaterial.CopyPropertiesFromMaterial(entry.Template);
            sharedMaterial.shaderKeywords = entry.Template.shaderKeywords;
            sharedMaterial.renderQueue = entry.Template.renderQueue;
            sharedMaterial.SetTexture(DoodleStickerShaderIds.MainTexture, entry.Texture);
            sharedMaterial.SetTexture(DoodleStickerShaderIds.AlphaTexture, entry.AlphaTexture);

            if (entry.AlphaTexture != null)
            {
                sharedMaterial.EnableKeyword(DoodleStickerShaderIds.ExternalAlphaKeyword);
            }
            else
            {
                sharedMaterial.DisableKeyword(DoodleStickerShaderIds.ExternalAlphaKeyword);
            }
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void SynchronizeTemplatesInEditor()
        {
            UnityEditor.EditorApplication.update -= RefreshFromTemplates;
            UnityEditor.EditorApplication.update += RefreshFromTemplates;
        }
#endif
    }
}
