using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace UmMegabonkStudio
{
    // Same accept/reject rules as um.megabonk_studio.interpret_model_message.
    // A model reply is not applied by this class.
    static class AiProtocol
    {
        static readonly string[] CharacterKeys = {
            "name", "description", "author", "appearance", "tint", "passive", "weapon", "portrait", "stats",
        };
        static readonly string[] StatKeys = { "stat", "modify", "amount" };
        static readonly string[] MessageKeys = { "type", "questions", "character" };

        public static bool TryInterpret(string text, out string kind, out List<string> questions, out StudioCharacter draft, out string error)
        {
            kind = "";
            questions = null;
            draft = null;
            error = "";
            if (!TryExtract(text, out var root, out error)) return false;
            foreach (var prop in root.EnumerateObject())
            {
                if (IndexOf(MessageKeys, prop.Name) < 0)
                {
                    error = "unknown message fields: " + prop.Name;
                    return false;
                }
            }
            if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            {
                error = "type must be questions or draft";
                return false;
            }
            var type = typeEl.GetString();
            if (type == "questions")
            {
                if (root.TryGetProperty("character", out _))
                {
                    error = "a question reply must not include a character";
                    return false;
                }
                if (!root.TryGetProperty("questions", out var list) || list.ValueKind != JsonValueKind.Array)
                {
                    error = "questions must be a list of 1 to 8 strings";
                    return false;
                }
                questions = new List<string>();
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        error = "questions must be a list of 1 to 8 strings";
                        return false;
                    }
                    questions.Add(item.GetString().Trim());
                }
                if (questions.Count < 1 || questions.Count > 8)
                {
                    error = "questions must be a list of 1 to 8 strings";
                    return false;
                }
                kind = "questions";
                return true;
            }
            if (type == "draft")
            {
                if (root.TryGetProperty("questions", out _))
                {
                    error = "a draft reply must not include questions";
                    return false;
                }
                if (!root.TryGetProperty("character", out var character) || character.ValueKind != JsonValueKind.Object)
                {
                    error = "character must be an object";
                    return false;
                }
                if (!TryDraft(character, out draft, out error)) return false;
                kind = "draft";
                return true;
            }
            error = "type must be questions or draft";
            return false;
        }

        static bool TryDraft(JsonElement row, out StudioCharacter draft, out string error)
        {
            draft = null;
            error = "";
            foreach (var prop in row.EnumerateObject())
            {
                if (prop.Name == "id")
                {
                    error = "the model must not choose an id";
                    return false;
                }
                if (IndexOf(CharacterKeys, prop.Name) < 0)
                {
                    error = "unknown character fields: " + prop.Name;
                    return false;
                }
            }
            if (!Required(row, "name", out var name, out error)) return false;
            if (!Required(row, "description", out var description, out error)) return false;
            if (!Required(row, "author", out var author, out error)) return false;
            if (!row.TryGetProperty("appearance", out var appearanceEl) || appearanceEl.ValueKind != JsonValueKind.String
                || Catalog.IndexOf(Catalog.Characters, appearanceEl.GetString()) < 0)
            {
                error = "appearance is not a published name";
                return false;
            }
            var tint = Optional(row, "tint");
            if (tint.Length != 0 && !HexTint(tint))
            {
                error = "tint must be empty or #RRGGBB";
                return false;
            }
            var passive = Optional(row, "passive");
            if (passive.Length != 0 && Catalog.IndexOf(Catalog.Passives, passive) < 0)
            {
                error = "passive " + passive + " is not a published name";
                return false;
            }
            var weapon = Optional(row, "weapon");
            if (weapon.Length != 0 && Catalog.IndexOf(Catalog.Weapons, weapon) < 0)
            {
                error = "weapon " + weapon + " is not a published name";
                return false;
            }
            var portrait = Optional(row, "portrait");
            if (portrait.StartsWith("/") || portrait.StartsWith("\\") || portrait.Contains("..") || portrait.Contains("://"))
            {
                error = "portrait must be empty or a relative file name";
                return false;
            }
            var stats = new List<StudioStat>();
            if (row.TryGetProperty("stats", out var statEl) && statEl.ValueKind != JsonValueKind.Null)
            {
                if (statEl.ValueKind != JsonValueKind.Array)
                {
                    error = "stats must be a list";
                    return false;
                }
                foreach (var stat in statEl.EnumerateArray())
                {
                    if (stat.ValueKind != JsonValueKind.Object)
                    {
                        error = "stats must be objects";
                        return false;
                    }
                    foreach (var prop in stat.EnumerateObject())
                    {
                        if (IndexOf(StatKeys, prop.Name) < 0)
                        {
                            error = "unknown stat fields: " + prop.Name;
                            return false;
                        }
                    }
                    if (!stat.TryGetProperty("stat", out var statName) || Catalog.IndexOf(Catalog.Stats, statName.GetString()) < 0)
                    {
                        error = "stat is not a published name";
                        return false;
                    }
                    if (!stat.TryGetProperty("modify", out var modify) || Catalog.IndexOf(Catalog.ModifyTypes, modify.GetString()) < 0)
                    {
                        error = "modify is not a published name";
                        return false;
                    }
                    if (!stat.TryGetProperty("amount", out var amount) || amount.ValueKind != JsonValueKind.Number)
                    {
                        error = "amount must be a number";
                        return false;
                    }
                    float number;
                    if (amount.TryGetSingle(out var single)) number = single;
                    else if (amount.TryGetDouble(out var wide)) number = (float)wide;
                    else
                    {
                        error = "amount must be a number";
                        return false;
                    }
                    var canonStat = Catalog.Stats[Catalog.IndexOf(Catalog.Stats, statName.GetString())];
                    foreach (var have in stats)
                    {
                        if (have.Stat == canonStat)
                        {
                            error = canonStat + " is listed twice";
                            return false;
                        }
                    }
                    stats.Add(new StudioStat
                    {
                        Stat = canonStat,
                        Modify = Catalog.ModifyTypes[Catalog.IndexOf(Catalog.ModifyTypes, modify.GetString())],
                        Amount = number,
                    });
                }
            }
            draft = new StudioCharacter
            {
                Name = name,
                Description = description,
                Author = author,
                Appearance = Catalog.Characters[Catalog.IndexOf(Catalog.Characters, appearanceEl.GetString())],
                Tint = tint,
                Passive = passive.Length == 0 ? "" : Catalog.Passives[Catalog.IndexOf(Catalog.Passives, passive)],
                Weapon = weapon.Length == 0 ? "" : Catalog.Weapons[Catalog.IndexOf(Catalog.Weapons, weapon)],
                Portrait = portrait,
                Stats = stats,
            };
            return true;
        }

        static bool HexTint(string tint)
        {
            if (tint.Length != 7 || tint[0] != '#') return false;
            for (int i = 1; i < tint.Length; i++)
            {
                var c = tint[i];
                var hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        static bool Required(JsonElement row, string key, out string value, out string error)
        {
            value = "";
            error = "";
            if (!row.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(el.GetString()))
            {
                error = key + " must be a non-empty string";
                return false;
            }
            value = el.GetString().Trim();
            return true;
        }

        static string Optional(JsonElement row, string key)
        {
            if (!row.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.String) return "";
            return el.GetString().Trim();
        }

        static bool TryExtract(string text, out JsonElement root, out string error)
        {
            root = default;
            error = "";
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "model reply has no JSON object with a type field";
                return false;
            }
            var raw = text.Trim();
            if (raw.StartsWith("```", StringComparison.Ordinal))
            {
                var start = raw.IndexOf('\n');
                var end = raw.LastIndexOf("```", StringComparison.Ordinal);
                if (start >= 0 && end > start) raw = raw.Substring(start + 1, end - start - 1);
            }
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] != '{') continue;
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(raw.Substring(i));
                    var reader = new Utf8JsonReader(bytes);
                    if (!JsonDocument.TryParseValue(ref reader, out var doc)) continue;
                    using (doc)
                    {
                        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("type", out _))
                        {
                            root = doc.RootElement.Clone();
                            return true;
                        }
                    }
                }
                catch (JsonException)
                {
                }
            }
            error = "model reply has no JSON object with a type field";
            return false;
        }

        static int IndexOf(string[] table, string name)
        {
            for (int i = 0; i < table.Length; i++)
                if (string.Equals(table[i], name, StringComparison.Ordinal)) return i;
            return -1;
        }

        public static string CatalogPrompt()
        {
            return "You help design a Megabonk character. You can only use the lists below. "
                + "A passive is one of the existing effects, not a new behaviour. "
                + "A weapon is one of the existing weapons. Appearance is a vanilla character to clone. "
                + "A new 3D model cannot be imported. "
                + "If the request is broad, reply with only this JSON: {\"type\":\"questions\",\"questions\":[\"...\"]}. "
                + "Ask 3 to 6 specific questions. "
                + "When you know enough, reply with only this JSON: {\"type\":\"draft\",\"character\":{...}}. "
                + "The character object may contain only name, description, author, appearance, tint, passive, weapon, portrait, stats. "
                + "Do not include id. tint is empty or #RRGGBB. portrait is empty. "
                + "stats is a list of {stat, modify, amount}. modify is Addition, Multiplication, or Flat. "
                + "Addition adds to the additive component. Multiplication affects the multiplicative component. "
                + "Flat is the third published mode; its in-game formula was not re-measured. "
                + "CritChance uses 1 for 100%. Do not invent stats.\n"
                + "Appearances: " + string.Join(", ", Catalog.Characters) + "\n"
                + "Passives: " + string.Join(", ", Catalog.Passives) + "\n"
                + "Weapons: " + string.Join(", ", Catalog.Weapons) + "\n"
                + "Stats: " + string.Join(", ", Catalog.Stats);
        }
    }
}
