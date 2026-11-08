using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace UmMegabonkStudio
{
    // IMGUI on this build cannot use a text box. TextEditor.SaveBackup is stripped and throws
    // Method unstripping failed, and the exception used to skip EndScrollView, which then
    // spammed "pushing more GUIClips than popping". Editing is buttons only.
    public class StudioBehaviour : MonoBehaviour
    {
        public StudioBehaviour(IntPtr ptr) : base(ptr) { }

        void Update()
        {
            if (Plugin.UiStopped) return;
            try
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.F7))
                    StudioWindow.RequestToggle();
            }
            catch (Exception ex)
            {
                Plugin.FailUi("Update", ex);
            }
        }

        void OnGUI()
        {
            if (Plugin.UiStopped) return;
            try { StudioWindow.Draw(); }
            catch (Exception ex)
            {
                StudioWindow.EndOpenGroups();
                Plugin.FailUi("OnGUI", ex);
            }
        }
    }

    static class StudioWindow
    {
        static bool _open;
        static bool _wasOpen;
        static bool _area;
        static bool _scroll;
        static int _toggleFrame = -2;
        static Rect _rect;
        static Vector2 _scrollPos;
        static int _index;
        static StudioFile _file;
        static bool _logged;
        static bool _editName;
        static string _status = "The editor is closed until you press F7.";
        static GUIStyle _label;
        static GUIStyle _button;
        static int _styleSize;

        public static void Install()
        {
            ClassInjector.RegisterTypeInIl2Cpp<StudioBehaviour>();
            var go = new GameObject("UmMegabonkStudio");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<StudioBehaviour>();
            Plugin.Info("Studio editor is closed. Press F7 to open it. Nothing is drawn while it is closed.");
        }

        public static void RequestToggle()
        {
            if (Plugin.UiStopped) return;
            if (Time.frameCount == _toggleFrame) return;
            _toggleFrame = Time.frameCount;
            _open = !_open;
            if (_open)
            {
                Reload();
                _editName = false;
                Plugin.Info("Studio editor opened.");
            }
            else
            {
                ReleaseInput();
                Plugin.Info("Studio editor closed.");
            }
        }

        public static void EndOpenGroups()
        {
            if (_scroll)
            {
                _scroll = false;
                try { GUILayout.EndScrollView(); } catch (Exception) { }
            }
            if (_area)
            {
                _area = false;
                try { GUILayout.EndArea(); } catch (Exception) { }
            }
            ReleaseInput();
            _open = false;
        }

        static void ReleaseInput()
        {
            try
            {
                GUIUtility.keyboardControl = 0;
                GUIUtility.hotControl = 0;
            }
            catch (Exception) { }
        }

        static void Reload()
        {
            _file = Drafts.Load(Plugin.JsonPath);
        }

        public static void Draw()
        {
            if (!_logged)
            {
                _logged = true;
                Plugin.Info("Studio UI OnGUI ran.");
            }
            var ev = Event.current;
            if (ev != null && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.F7)
            {
                RequestToggle();
                ev.Use();
                return;
            }
            if (!_open)
            {
                if (_wasOpen) ReleaseInput();
                _wasOpen = false;
                return;
            }
            _wasOpen = true;
            Styles();
            _area = false;
            _scroll = false;
            _rect = new Rect(Screen.width * 0.08f, Screen.height * 0.06f, Screen.width * 0.84f, Screen.height * 0.88f);
            GUILayout.BeginArea(_rect, GUI.skin.window);
            _area = true;
            try
            {
                _scrollPos = GUILayout.BeginScrollView(_scrollPos);
                _scroll = true;
                try { DrawBody(); }
                finally
                {
                    GUILayout.EndScrollView();
                    _scroll = false;
                }
            }
            finally
            {
                GUILayout.EndArea();
                _area = false;
            }
        }

        static void DrawBody()
        {
            GUILayout.Label("Megabonk Studio", _label);
            GUILayout.Label("F7 or Close hides this panel. While it is closed, this plugin does not draw and does not keep a text cursor.", _label);
            GUILayout.Label(_status, _label);
            if (GUILayout.Button("Close (F7)", _button))
                RequestToggle();
            if (_file == null) Reload();
            var rows = _file.Characters;
            if (rows.Count == 0) GUILayout.Label("No characters in " + Plugin.JsonPath, _label);
            for (int i = 0; i < rows.Count; i++)
            {
                var mark = i == _index ? "Selected: " : "Select: ";
                if (GUILayout.Button(mark + rows[i].Name + "    look " + rows[i].Appearance, _button))
                    _index = i;
            }
            if (rows.Count == 0) return;
            if (_index < 0 || _index >= rows.Count) _index = 0;
            var cur = rows[_index];
            GUILayout.Label("Appearance, passive, and weapon are the game's existing ones. Click a row to change it.", _label);
            if (GUILayout.Button("Appearance:  " + cur.Appearance, _button))
                cur.Appearance = Step(Catalog.Characters, cur.Appearance, 1);
            if (GUILayout.Button("Passive:  " + (string.IsNullOrEmpty(cur.Passive) ? "keep " + cur.Appearance : cur.Passive), _button))
                cur.Passive = Step(WithBlank(Catalog.Passives), cur.Passive, 1);
            if (GUILayout.Button("Weapon:  " + (string.IsNullOrEmpty(cur.Weapon) ? "keep " + cur.Appearance : cur.Weapon), _button))
                cur.Weapon = Step(WithBlank(Catalog.Weapons), cur.Weapon, 1);
            GUILayout.Label("Name:  " + cur.Name, _label);
            if (GUILayout.Button(_editName ? "Done editing name" : "Edit name", _button))
                _editName = !_editName;
            if (_editName) NameKeys(cur);
            GUILayout.Label("Extra stats on top of " + cur.Appearance + ". The buttons change the number. Nothing here uses a text box.", _label);
            if (cur.Stats == null) cur.Stats = new List<StudioStat>();
            for (int i = 0; i < cur.Stats.Count; i++)
            {
                var stat = cur.Stats[i];
                GUILayout.Label(stat.Stat + "  " + stat.Modify + "  " + stat.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), _label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(stat.Stat, _button))
                    stat.Stat = Step(Catalog.Stats, stat.Stat, 1);
                if (GUILayout.Button("-1", _button)) stat.Amount -= 1f;
                if (GUILayout.Button("+1", _button)) stat.Amount += 1f;
                if (GUILayout.Button("-0.1", _button)) stat.Amount -= 0.1f;
                if (GUILayout.Button("+0.1", _button)) stat.Amount += 0.1f;
                if (GUILayout.Button("Remove", _button))
                {
                    cur.Stats.RemoveAt(i);
                    i--;
                }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Add stat", _button))
                cur.Stats.Add(new StudioStat { Stat = "MaxHealth", Modify = "Addition", Amount = 0f });
            if (GUILayout.Button("Save", _button))
                Save();
        }

        static void NameKeys(StudioCharacter cur)
        {
            const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            GUILayout.BeginHorizontal();
            for (int i = 0; i < letters.Length; i++)
            {
                if (i == 13)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
                var ch = letters.Substring(i, 1);
                if (GUILayout.Button(ch, _button))
                    cur.Name = (cur.Name ?? "") + ch;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Space", _button)) cur.Name = (cur.Name ?? "") + " ";
            if (GUILayout.Button("Backspace", _button) && !string.IsNullOrEmpty(cur.Name))
                cur.Name = cur.Name.Substring(0, cur.Name.Length - 1);
            GUILayout.EndHorizontal();
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
                _status = "Saved. A different appearance shows after a restart. Close this panel with F7 before playing.";
            }
            catch (Exception ex)
            {
                _status = "Save failed: " + ex.Message;
                Plugin.Warn(_status);
            }
        }

        static void Styles()
        {
            int size = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 32f), 22, 40);
            if (_label != null && _styleSize == size) return;
            _styleSize = size;
            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = size;
            _label.wordWrap = true;
            _button = new GUIStyle(GUI.skin.button);
            _button.fontSize = size;
            _button.fixedHeight = size * 2.1f;
        }

        static string Step(string[] values, string current, int dir)
        {
            int i = 0;
            for (int n = 0; n < values.Length; n++)
            {
                if (string.Equals(values[n], current ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    i = n;
                    break;
                }
            }
            i = (i + dir + values.Length) % values.Length;
            return values[i];
        }

        static string[] WithBlank(string[] values)
        {
            var next = new string[values.Length + 1];
            next[0] = "";
            Array.Copy(values, 0, next, 1, values.Length);
            return next;
        }
    }
}
