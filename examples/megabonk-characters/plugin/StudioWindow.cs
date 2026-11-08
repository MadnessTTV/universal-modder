using System;
using System.Collections.Generic;
using System.Text.Json;
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
                AiClient.Poll();
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
            _rect = new Rect(Screen.width * 0.06f, Screen.height * 0.04f, Screen.width * 0.88f, Screen.height * 0.92f);
            Fill(_rect, new Color(0.04f, 0.05f, 0.07f, 1f));
            GUILayout.BeginArea(_rect);
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

        static string _menu = "";
        static string _filter = "";
        static string _prompt = "";
        static bool _editPrompt;
        static readonly List<string> _history = new List<string>();

        static void DrawBody()
        {
            GUILayout.Label("Megabonk Studio", _label);
            GUILayout.Label("F7 or Close hides this panel. While it is closed, this plugin does not draw.", _label);
            GUILayout.Label("A passive is one of the 21 existing effects. A weapon is one of the existing weapons. Appearance clones that character's model. A new 3D model and a new kind of ability are not available here.", _label);
            GUILayout.Label("Stat amounts are the game's own numbers. Addition adds to the additive component. Multiplication affects the multiplicative component. Flat is the third published mode; its formula was not re-measured. Crit chance treats 1 as 100%.", _label);
            GUILayout.Label(_status, _label);
            if (GUILayout.Button("Close (F7)", _button))
                RequestToggle();
            if (GUILayout.Button(_history.Count == 0 ? "Undo (nothing saved yet)" : "Undo last apply", _button) && _history.Count > 0)
                Undo();
            if (_file == null) Reload();
            var rows = _file.Characters;
            GUILayout.Label("Characters", _label);
            for (int i = 0; i < rows.Count; i++)
            {
                var mark = i == _index ? "Selected: " : "Select: ";
                if (GUILayout.Button(mark + rows[i].Name + "    look " + rows[i].Appearance, _button))
                {
                    _index = i;
                    _menu = "";
                }
            }
            if (rows.Count == 0)
            {
                ModelSection(null);
                return;
            }
            if (_index < 0 || _index >= rows.Count) _index = 0;
            var cur = rows[_index];
            Picker("Appearance", cur.Appearance, Catalog.Characters, value => cur.Appearance = value, false);
            var passiveShown = string.IsNullOrEmpty(cur.Passive) ? "(keep cloned passive)" : cur.Passive;
            Picker("Passive", passiveShown, WithBlank(Catalog.Passives), value => cur.Passive = value, true);
            var weaponShown = string.IsNullOrEmpty(cur.Weapon) ? "(keep cloned weapon)" : cur.Weapon;
            Picker("Weapon", weaponShown, WithBlank(Catalog.Weapons), value => cur.Weapon = value, true);
            GUILayout.Label("Tint is stored as a color. The 3D model keeps the cloned character's materials.", _label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(string.IsNullOrEmpty(cur.Tint) ? "Tint: none" : "Tint: " + cur.Tint, _button))
                cur.Tint = "";
            if (GUILayout.Button("Gold", _button)) cur.Tint = "#FFD24A";
            if (GUILayout.Button("Lilac", _button)) cur.Tint = "#E0C0FF";
            if (GUILayout.Button("White", _button)) cur.Tint = "#FFFFFF";
            GUILayout.EndHorizontal();
            GUILayout.Label("Name:  " + cur.Name, _label);
            if (GUILayout.Button(_editName ? "Done editing name" : "Edit name", _button))
            {
                _editName = !_editName;
                _editPrompt = false;
            }
            if (_editName) NameKeys(() => cur.Name ?? "", value => cur.Name = value);
            GUILayout.Label("Every supported stat. Unchanged means this character does not add an extra modifier.", _label);
            for (int s = 0; s < Catalog.Stats.Length; s++)
            {
                var name = Catalog.Stats[s];
                StudioStat have = null;
                if (cur.Stats != null)
                {
                    foreach (var stat in cur.Stats)
                        if (stat.Stat == name) have = stat;
                }
                var line = have == null
                    ? name + ": unchanged"
                    : name + ": " + have.Modify + " " + have.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                GUILayout.Label(line, _label);
                if (have != null)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Addition", _button)) have.Modify = "Addition";
                    if (GUILayout.Button("Multiplication", _button)) have.Modify = "Multiplication";
                    if (GUILayout.Button("Flat", _button)) have.Modify = "Flat";
                    if (GUILayout.Button("-1", _button)) have.Amount -= 1f;
                    if (GUILayout.Button("+1", _button)) have.Amount += 1f;
                    if (GUILayout.Button("-0.1", _button)) have.Amount -= 0.1f;
                    if (GUILayout.Button("+0.1", _button)) have.Amount += 0.1f;
                    if (GUILayout.Button("Remove", _button)) cur.Stats.Remove(have);
                    GUILayout.EndHorizontal();
                }
            }
            if (GUILayout.Button(_menu == "addstat" ? "Close stat list" : "Add a stat", _button))
            {
                _menu = _menu == "addstat" ? "" : "addstat";
                _filter = "";
            }
            if (_menu == "addstat")
            {
                FilterKeys();
                for (int s = 0; s < Catalog.Stats.Length; s++)
                {
                    var name = Catalog.Stats[s];
                    if (_filter.Length > 0 && name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (GUILayout.Button("Add " + name, _button))
                    {
                        if (cur.Stats == null) cur.Stats = new List<StudioStat>();
                        var exists = false;
                        foreach (var stat in cur.Stats) if (stat.Stat == name) exists = true;
                        if (!exists) cur.Stats.Add(new StudioStat { Stat = name, Modify = "Addition", Amount = 0f });
                        _menu = "";
                    }
                }
            }
            if (GUILayout.Button("Save manual edits", _button))
                Save();
            ModelSection(cur);
        }

        static void Picker(string label, string current, string[] values, Action<string> set, bool blankLabel)
        {
            var open = _menu == label;
            if (GUILayout.Button(label + ":  " + current + (open ? "    (close list)" : "    (open list)"), _button))
            {
                _menu = open ? "" : label;
                _filter = "";
            }
            if (!open) return;
            FilterKeys();
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                var shown = value.Length == 0 ? "(keep cloned)" : value;
                if (_filter.Length > 0 && shown.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button(shown, _button))
                {
                    set(blankLabel && value.Length == 0 ? "" : value);
                    _menu = "";
                }
            }
        }

        static void FilterKeys()
        {
            GUILayout.Label("Filter: " + (_filter.Length == 0 ? "(all)" : _filter), _label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear filter", _button)) _filter = "";
            if (GUILayout.Button("Backspace", _button) && _filter.Length > 0)
                _filter = _filter.Substring(0, _filter.Length - 1);
            GUILayout.EndHorizontal();
            LetterRow(ch => _filter += ch);
        }

        static bool _lower;

        static void NameKeys(Func<string> get, Action<string> assign)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Backspace", _button) && get().Length > 0)
                assign(get().Substring(0, get().Length - 1));
            if (GUILayout.Button("Space", _button))
                assign(get() + " ");
            if (GUILayout.Button(_lower ? "Letters: lower" : "Letters: UPPER", _button))
                _lower = !_lower;
            GUILayout.EndHorizontal();
            LetterRow(ch => assign(get() + ch));
            DigitRow(ch => assign(get() + ch));
        }

        static void ModelSection(StudioCharacter selected)
        {
            GUILayout.Label("Model", _label);
            GUILayout.Label(AiClient.Status, _label);
            if (!AiClient.TryConfig(out var baseUrl, out var model, out _, out _))
            {
                GUILayout.Label("No model is connected. Nothing is sent. To connect one, create ai.json beside characters.json with base_url and model. OpenAI's chat completions address is https://api.openai.com/v1 and the model id goes in model. The key stays in the MEGABONK_AI_KEY environment variable of this game process. Quit Thunderstore Mod Manager and start it again from a session that already has that variable. A manager that is already open does not see a variable added later. The key is not written into ai.json.", _label);
            }
            else
            {
                GUILayout.Label("Configured server " + baseUrl + ", model " + model + ". A key, when the server needs one, is read from the environment and is not shown or saved.", _label);
            }
            foreach (var question in AiClient.Questions)
                GUILayout.Label("Question: " + question, _label);
            if (AiClient.Pending != null)
            {
                var pending = AiClient.Pending;
                GUILayout.Label("Pending draft " + pending.Name + " looks like " + pending.Appearance
                    + ", passive " + (string.IsNullOrEmpty(pending.Passive) ? "(cloned)" : pending.Passive)
                    + ", weapon " + (string.IsNullOrEmpty(pending.Weapon) ? "(cloned)" : pending.Weapon)
                    + ". It is not in the character list until you apply it.", _label);
                if (GUILayout.Button("Apply as a new character", _button))
                    ApplyPending(null);
                if (selected != null && GUILayout.Button("Apply onto " + selected.Name + ", keeping its id", _button))
                    ApplyPending(selected);
                if (GUILayout.Button("Discard draft", _button))
                    AiClient.ClearPending();
            }
            GUILayout.Label("Prompt: " + (_prompt.Length == 0 ? "(empty)" : _prompt), _label);
            if (GUILayout.Button(_editPrompt ? "Done editing prompt" : "Edit prompt", _button))
            {
                _editPrompt = !_editPrompt;
                _editName = false;
            }
            if (_editPrompt)
                NameKeys(() => _prompt, value => _prompt = value);
            if (GUILayout.Button(AiClient.Busy ? "Waiting for the model" : "Send prompt", _button) && !AiClient.Busy)
            {
                var context = selected == null ? "" : JsonSerializer.Serialize(selected);
                AiClient.Ask(_prompt, context);
            }
        }

        static void ApplyPending(StudioCharacter onto)
        {
            var draft = AiClient.Pending;
            if (draft == null || _file == null) return;
            var slot = -1;
            if (onto != null)
            {
                for (int i = 0; i < _file.Characters.Count; i++)
                    if (_file.Characters[i].Id == onto.Id) slot = i;
                if (slot < 0)
                {
                    _status = "The selected character was not in the list. Nothing was written.";
                    return;
                }
            }
            Remember();
            if (onto == null)
            {
                int ident;
                if (_file.Characters.Count == 0)
                    ident = 1842201100;
                else
                {
                    ident = 1000;
                    foreach (var row in _file.Characters)
                        if (row.Id > ident) ident = row.Id;
                    ident += 1;
                }
                draft.Id = ident;
                _file.Characters.Add(draft);
                _index = _file.Characters.Count - 1;
            }
            else
            {
                draft.Id = onto.Id;
                _file.Characters[slot] = draft;
                _index = slot;
            }
            AiClient.Pending = null;
            Save();
            _status = "Applied the model draft. Undo restores the previous character list.";
        }

        static void Remember()
        {
            try
            {
                _history.Add(JsonSerializer.Serialize(_file));
                if (_history.Count > 20) _history.RemoveAt(0);
            }
            catch (Exception ex)
            {
                Plugin.Warn("Could not snapshot before apply: " + ex.Message);
            }
        }

        static void Undo()
        {
            var raw = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);
            _file = JsonSerializer.Deserialize<StudioFile>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (_file == null || _file.Characters == null)
            {
                Reload();
                _status = "Undo failed. The character list was reloaded from disk.";
                return;
            }
            if (_index >= _file.Characters.Count) _index = 0;
            Save();
            _status = "Restored the previous character list.";
        }

        static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static void LetterRow(Action<char> add)
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
                var ch = letters[i];
                var shown = _lower ? char.ToLowerInvariant(ch) : ch;
                if (GUILayout.Button(shown.ToString(), _button))
                    add(shown);
            }
            GUILayout.EndHorizontal();
        }

        static void DigitRow(Action<char> add)
        {
            GUILayout.BeginHorizontal();
            const string digits = "0123456789";
            for (int i = 0; i < digits.Length; i++)
            {
                var ch = digits[i];
                if (GUILayout.Button(ch.ToString(), _button))
                    add(ch);
            }
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
            _label.normal.textColor = Color.white;
            _button = new GUIStyle(GUI.skin.button);
            _button.fontSize = size;
            _button.fixedHeight = size * 2.1f;
            _button.wordWrap = true;
            // This install's RectOffset has a parameterless constructor and left/right/top/bottom setters.
            var border = new RectOffset();
            border.left = 0;
            border.right = 0;
            border.top = 0;
            border.bottom = 0;
            _button.border = border;
            Paint(_button.normal);
            Paint(_button.hover);
            Paint(_button.active);
            Paint(_button.focused);
            Paint(_button.onNormal);
            Paint(_button.onHover);
            Paint(_button.onActive);
            Paint(_button.onFocused);
        }

        static void Paint(GUIStyleState state)
        {
            if (state == null) return;
            state.background = Texture2D.whiteTexture;
            state.textColor = Color.black;
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
