using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace UmMegabonkStudio
{
    // IMGUI editor. OnGUI running in this game was not verified when this source was written.
    // The first successful frame logs "Studio UI OnGUI ran". Until that line is in LogOutput.log,
    // use `um megabonk studio` and restart.
    public class StudioBehaviour : MonoBehaviour
    {
        public StudioBehaviour(IntPtr ptr) : base(ptr) { }

        void Update()
        {
            try
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.F7))
                    StudioWindow.Toggle();
            }
            catch (Exception ex)
            {
                Plugin.FailUi("Update", ex);
            }
        }

        void OnGUI()
        {
            try { StudioWindow.Draw(); }
            catch (Exception ex) { Plugin.FailUi("OnGUI", ex); }
        }
    }

    static class StudioWindow
    {
        static bool _open;
        static Rect _rect = new Rect(40f, 40f, 560f, 640f);
        static Vector2 _scroll;
        static int _index;
        static StudioFile _file;
        static bool _drew;
        static string _status = "F7 toggles this window. Edits save to characters.json.";

        public static void Install()
        {
            ClassInjector.RegisterTypeInIl2Cpp<StudioBehaviour>();
            var go = new GameObject("UmMegabonkStudio");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<StudioBehaviour>();
        }

        public static void Toggle()
        {
            _open = !_open;
            if (_open) Reload();
        }

        static void Reload()
        {
            _file = Drafts.Load(Plugin.JsonPath);
        }

        public static void Draw()
        {
            if (!_drew)
            {
                _drew = true;
                Plugin.Info("Studio UI OnGUI ran.");
            }
            if (!_open) return;
            if (_file == null) Reload();
            GUI.Box(_rect, "Megabonk Studio");
            GUILayout.BeginArea(new Rect(_rect.x + 8f, _rect.y + 28f, _rect.width - 16f, _rect.height - 36f));
            DrawBody(0);
            GUILayout.EndArea();
            var e = Event.current;
            var bar = new Rect(_rect.x, _rect.y, _rect.width, 24f);
            if (e != null && e.type == EventType.MouseDrag && bar.Contains(e.mousePosition))
            {
                _rect.x += e.delta.x;
                _rect.y += e.delta.y;
            }
        }

        static void DrawBody(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("Clones a vanilla character. New abilities are existing passives. A brand-new effect is not created here.");
            GUILayout.Label("A different appearance applies on the next launch. Passive, weapon, tint and stats apply to the next run.");
            GUILayout.Label(_status);
            var rows = _file.Characters;
            if (rows.Count == 0) GUILayout.Label("No characters in " + Plugin.JsonPath);
            for (int i = 0; i < rows.Count; i++)
            {
                if (GUILayout.Button((i == _index ? "> " : "  ") + rows[i].Name + "  (" + rows[i].Appearance + ")"))
                    _index = i;
            }
            if (GUILayout.Button("New character"))
            {
                var row = new StudioCharacter
                {
                    Id = NextId(rows),
                    Name = "New character",
                    Description = "Cloned from Fox.",
                    Author = "local",
                    Appearance = "Fox",
                };
                rows.Add(row);
                _index = rows.Count - 1;
            }
            if (rows.Count > 0)
            {
                if (_index < 0 || _index >= rows.Count) _index = 0;
                var cur = rows[_index];
                GUILayout.Label("Id " + cur.Id + " (id is fixed)");
                GUILayout.Label("Name");
                cur.Name = GUILayout.TextField(cur.Name ?? "");
                GUILayout.Label("Description");
                cur.Description = GUILayout.TextArea(cur.Description ?? "", GUILayout.MinHeight(48f));
                Cycle("Appearance", Catalog.Characters, cur.Appearance, value => cur.Appearance = value);
                Cycle("Passive (blank keeps the clone)", WithBlank(Catalog.Passives), cur.Passive, value => cur.Passive = value);
                Cycle("Weapon (blank keeps the clone)", WithBlank(Catalog.Weapons), cur.Weapon, value => cur.Weapon = value);
                GUILayout.Label("Tint #RRGGBB, or empty");
                cur.Tint = GUILayout.TextField(cur.Tint ?? "");
                GUILayout.Label("Extra stats, on top of " + cur.Appearance);
                if (cur.Stats == null) cur.Stats = new List<StudioStat>();
                for (int i = 0; i < cur.Stats.Count; i++)
                {
                    var stat = cur.Stats[i];
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(stat.Stat, GUILayout.Width(220f)))
                        stat.Stat = Step(Catalog.Stats, stat.Stat, 1);
                    if (GUILayout.Button(stat.Modify, GUILayout.Width(140f)))
                        stat.Modify = Step(Catalog.ModifyTypes, stat.Modify, 1);
                    var amount = GUILayout.TextField(stat.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), GUILayout.Width(80f));
                    if (float.TryParse(amount, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                        stat.Amount = number;
                    if (GUILayout.Button("x", GUILayout.Width(28f)))
                    {
                        cur.Stats.RemoveAt(i);
                        i--;
                    }
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Add stat"))
                    cur.Stats.Add(new StudioStat { Stat = "MaxHealth", Modify = "Addition", Amount = 0f });
            }
            if (GUILayout.Button("Save"))
                Save();
            GUILayout.EndScrollView();
        }

        static void Save()
        {
            try
            {
                Drafts.Save(Plugin.JsonPath, _file);
                var dm = Registrar.LastManager;
                if (dm != null && _file.Characters.Count > 0)
                {
                    var cur = _file.Characters[Math.Max(0, Math.Min(_index, _file.Characters.Count - 1))];
                    Registrar.ApplyLive(dm, Plugin.JsonPath, cur);
                }
                _status = "Saved. Restart so a new character or a new appearance shows in the grid.";
            }
            catch (Exception ex)
            {
                _status = "Save failed: " + ex.Message;
                Plugin.Warn(_status);
            }
        }

        static void Cycle(string label, string[] values, string current, Action<string> set)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(220f));
            if (GUILayout.Button("<", GUILayout.Width(28f))) set(Step(values, current, -1));
            GUILayout.Label(string.IsNullOrEmpty(current) ? "(cloned)" : current, GUILayout.Width(180f));
            if (GUILayout.Button(">", GUILayout.Width(28f))) set(Step(values, current, 1));
            GUILayout.EndHorizontal();
        }

        static string Step(string[] values, string current, int dir)
        {
            int i = Catalog.IndexOf(values, current);
            if (i < 0) i = 0;
            else i = (i + dir + values.Length) % values.Length;
            return values[i];
        }

        static string[] WithBlank(string[] values)
        {
            var next = new string[values.Length + 1];
            next[0] = "";
            Array.Copy(values, 0, next, 1, values.Length);
            return next;
        }

        static int NextId(List<StudioCharacter> rows)
        {
            int id = 1842201100;
            foreach (var row in rows)
                if (row.Id >= id) id = row.Id + 1;
            return id;
        }
    }
}
