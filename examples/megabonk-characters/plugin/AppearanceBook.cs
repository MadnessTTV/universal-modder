using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UmMegabonkStudio
{
    // Named looks. This file is not characters.json and does not change those rows.
    sealed class AppearancePreset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("appearance")] public string Appearance { get; set; } = "";
        [JsonPropertyName("tint")] public string Tint { get; set; } = "";
        [JsonPropertyName("paint")] public bool Paint { get; set; }
    }

    sealed class AppearanceFile
    {
        [JsonPropertyName("format")] public string Format { get; set; } = "um-megabonk-appearance-presets";
        [JsonPropertyName("format_version")] public int FormatVersion { get; set; } = 1;
        [JsonPropertyName("presets")] public List<AppearancePreset> Presets { get; set; } = new List<AppearancePreset>();
    }

    static class AppearanceBook
    {
        public static string PathFor(string charactersPath)
        {
            return Path.Combine(Path.GetDirectoryName(charactersPath) ?? "", "appearance-presets.json");
        }

        public static AppearanceFile Load(string path)
        {
            if (!File.Exists(path)) return new AppearanceFile();
            var file = JsonSerializer.Deserialize<AppearanceFile>(File.ReadAllText(path));
            if (file == null || file.Presets == null) return new AppearanceFile();
            if (file.Format != "um-megabonk-appearance-presets" || file.FormatVersion != 1)
                return new AppearanceFile();
            return file;
        }

        public static void Save(string path, AppearanceFile file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(file, opts) + "\n");
        }

        public static string Problem(AppearancePreset preset, AppearanceFile existing)
        {
            if (preset == null) return "preset must be an object";
            var name = (preset.Name ?? "").Trim();
            if (name.Length < 1 || name.Length > 40 || name.IndexOf('\n') >= 0)
                return "preset name must be 1 to 40 characters on one line";
            if (existing != null)
            {
                foreach (var row in existing.Presets)
                    if (row != null && row.Name == name) return "a preset with that name is already saved";
            }
            if (Catalog.IndexOf(Catalog.Characters, preset.Appearance) < 0)
                return "appearance is not a published body";
            var tint = preset.Tint ?? "";
            if (tint.Length != 0 && !Hex(tint)) return "tint must be empty or #RRGGBB";
            if (preset.Paint && !Hex(tint)) return "paint requires tint #RRGGBB";
            return null;
        }

        public static bool Hex(string tint)
        {
            if (tint == null || tint.Length != 7 || tint[0] != '#') return false;
            for (int i = 1; i < 7; i++)
            {
                var c = tint[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        public static string NormalizeHex(string text)
        {
            if (text == null) return null;
            text = text.Trim();
            if (text.Length == 6) text = "#" + text;
            if (!Hex(text)) return null;
            return "#" + text.Substring(1).ToUpperInvariant();
        }
    }
}
