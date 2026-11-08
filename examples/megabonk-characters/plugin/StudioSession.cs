using System;
using System.Collections.Generic;

namespace UmMegabonkStudio
{
    // Same screens as um.megabonk_prompt.StudioSession.
    // Create has no edit id. Edit and refine do not apply as a new character.
    sealed class StudioSession
    {
        public string Intent = "home";
        public string Screen = "home";
        public int EditId;
        public bool HasEditId;
        public int CreatedId;
        public bool HasCreatedId;

        public void BeginCreate()
        {
            Intent = "create";
            Screen = "create_prompt";
            HasEditId = false;
        }

        public void BeginEdit(int ident)
        {
            Intent = "edit";
            Screen = "edit_home";
            EditId = ident;
            HasEditId = true;
        }

        public void BeginManual()
        {
            Screen = "manual";
        }

        public void BeginRefine()
        {
            if (!HasCreatedId) return;
            Intent = "refine";
            Screen = "refine_prompt";
            EditId = CreatedId;
            HasEditId = true;
        }

        public void NoteReply(string kind)
        {
            if (Intent == "create")
            {
                Screen = kind == "questions" ? "create_questions" : "create_preview";
                return;
            }
            if (Intent == "edit" || Intent == "refine")
                Screen = kind == "questions" ? "edit_questions" : "edit_preview";
        }

        public bool TryApplyNew(out bool asNew)
        {
            asNew = false;
            if (Intent == "create" && Screen == "create_preview")
            {
                asNew = true;
                return true;
            }
            return false;
        }

        public bool TryApplyEdit(out int id)
        {
            id = 0;
            if ((Intent == "edit" || Intent == "refine") && Screen == "edit_preview" && HasEditId)
            {
                id = EditId;
                return true;
            }
            return false;
        }

        public void NoteCreated(int ident)
        {
            CreatedId = ident;
            HasCreatedId = true;
            Screen = "refine_offer";
            Intent = "home";
            HasEditId = false;
        }

        public void Home()
        {
            Intent = "home";
            Screen = "home";
            HasEditId = false;
        }
    }

    static class DraftDiff
    {
        public static List<string> Describe(StudioCharacter before, StudioCharacter draft)
        {
            var lines = new List<string>();
            Field(lines, "Name", before == null ? "" : before.Name, draft.Name);
            Field(lines, "Description", before == null ? "" : before.Description, draft.Description);
            Field(lines, "Author", before == null ? "" : before.Author, draft.Author);
            Field(lines, "Look", before == null ? "" : before.Appearance, draft.Appearance);
            Field(lines, "Passive", before == null ? "" : before.Passive, draft.Passive);
            Field(lines, "Weapon", before == null ? "" : before.Weapon, draft.Weapon);
            Field(lines, "Tint", before == null ? "" : before.Tint, draft.Tint);
            var oldStats = Stats(before == null ? null : before.Stats);
            var newStats = Stats(draft.Stats);
            if (oldStats != newStats)
                lines.Add("Stats: " + oldStats + " -> " + newStats);
            if (lines.Count == 0)
                lines.Add("No field differs from the current character.");
            return lines;
        }

        static void Field(List<string> lines, string label, string oldValue, string newValue)
        {
            oldValue = oldValue ?? "";
            newValue = newValue ?? "";
            if (oldValue == newValue) return;
            lines.Add(label + ": " + (oldValue.Length == 0 ? "(empty)" : oldValue) + " -> " + (newValue.Length == 0 ? "(empty)" : newValue));
        }

        static string Stats(List<StudioStat> stats)
        {
            if (stats == null || stats.Count == 0) return "(none)";
            var parts = new string[stats.Count];
            for (int i = 0; i < stats.Count; i++)
            {
                var row = stats[i];
                parts[i] = row.Stat + " " + row.Modify + " " + row.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return string.Join(", ", parts);
        }
    }
}
