using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UmMegabonkStudio
{
    // Same json as `um megabonk studio`. Field names are the contract.
    sealed class StudioFile
    {
        [JsonPropertyName("format")] public string Format { get; set; } = "um-megabonk-studio";
        [JsonPropertyName("format_version")] public int FormatVersion { get; set; } = 1;
        [JsonPropertyName("characters")] public List<StudioCharacter> Characters { get; set; } = new List<StudioCharacter>();
    }

    sealed class StudioCharacter
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("author")] public string Author { get; set; } = "local";
        [JsonPropertyName("appearance")] public string Appearance { get; set; } = "Fox";
        [JsonPropertyName("tint")] public string Tint { get; set; } = "";
        [JsonPropertyName("passive")] public string Passive { get; set; } = "";
        [JsonPropertyName("weapon")] public string Weapon { get; set; } = "";
        [JsonPropertyName("portrait")] public string Portrait { get; set; } = "";
        [JsonPropertyName("stats")] public List<StudioStat> Stats { get; set; } = new List<StudioStat>();
    }

    sealed class StudioStat
    {
        [JsonPropertyName("stat")] public string Stat { get; set; } = "MaxHealth";
        [JsonPropertyName("modify")] public string Modify { get; set; } = "Addition";
        [JsonPropertyName("amount")] public float Amount { get; set; }
    }

    static class Drafts
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };

        public static StudioFile Load(string path)
        {
            if (!File.Exists(path))
                return new StudioFile();
            var text = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<StudioFile>(text, Options) ?? new StudioFile();
            if (file.Characters == null) file.Characters = new List<StudioCharacter>();
            foreach (var row in file.Characters)
            {
                if (row.Stats == null) row.Stats = new List<StudioStat>();
                if (row.Name == null) row.Name = "";
                if (row.Description == null) row.Description = "";
                if (row.Author == null) row.Author = "local";
                if (row.Appearance == null) row.Appearance = "";
                if (row.Tint == null) row.Tint = "";
                if (row.Passive == null) row.Passive = "";
                if (row.Weapon == null) row.Weapon = "";
                if (row.Portrait == null) row.Portrait = "";
            }
            return file;
        }

        public static void Save(string path, StudioFile file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(file, Options) + "\n");
        }
    }
}
