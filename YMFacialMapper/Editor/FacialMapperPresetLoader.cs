using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YoridoriModifiers.Core.Editor;

namespace YoridoriModifiers.FacialMapper
{
    internal static class FacialMapperPresetLoader
    {
        private const string ToolName = "YM Facial Mapper";
        private const string DefaultPresetGuid = "f403ca6ce1014811a8c1f08f730e0601";
        internal const string UserPresetPath = "Assets/YM-Facial-Mapper-Presets.json";

        [Serializable]
        internal sealed class PresetFile
        {
            public int version;
            public Preset[] presets;
        }

        [Serializable]
        internal sealed class Preset
        {
            public string name;
            public string memo;
            public Slot neutral;
            public HandSignPreset[] handSigns;
        }

        [Serializable]
        internal sealed class HandSignPreset
        {
            public string sign;
            public Slot left;
            public Slot right;
        }

        [Serializable]
        internal sealed class Slot
        {
            public bool eyelidLeft;
            public bool eyelidRight;
            public bool viseme;
            public string[] shapeKeys;
        }

        internal static List<Preset> LoadPresets(bool verbose)
        {
            var presets = new List<Preset>();
            LoadFromPath(UserPresetPath, "User", presets, verbose);

            var defaultPath = AssetDatabase.GUIDToAssetPath(DefaultPresetGuid);
            if (!string.IsNullOrWhiteSpace(defaultPath))
            {
                LoadFromPath(defaultPath, "Default", presets, verbose);
            }

            if (presets.Count == 0)
            {
                presets.Add(new Preset { name = "Empty" });
            }

            return presets;
        }

        private static void LoadFromPath(string path, string source, List<Preset> presets, bool verbose)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null) return;

            PresetFile file;
            try
            {
                file = JsonUtility.FromJson<PresetFile>(asset.text);
            }
            catch (Exception ex)
            {
                LogUtility.Warning(ToolName, "Preset", $"Failed to parse {source} preset JSON: {ex.Message} ({path})");
                return;
            }

            if (file?.presets == null) return;
            foreach (var preset in file.presets)
            {
                if (preset == null || string.IsNullOrWhiteSpace(preset.name)) continue;
                presets.Add(preset);
            }

            LogUtility.Verbose(ToolName, verbose, "Preset", $"Loaded {file.presets.Length} presets from {source}: {path}");
        }
    }
}
