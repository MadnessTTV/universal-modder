using System;
using System.Collections.Generic;
using System.Text.Json;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace UmMegabonkStudio
{
    // The stripped text widget is not used. Typed characters come from Event.current
    // into PromptBuffer. A hidden panel does not read keys and does not block the game.
    public class StudioBehaviour : MonoBehaviour
    {
        public StudioBehaviour(IntPtr ptr) : base(ptr) { }

        void Update()
        {
            if (Plugin.UiStopped) return;
            try
            {
                AiClient.Poll();
                if (!StudioWindow.BlockGameplayKeys && UnityEngine.Input.GetKeyDown(KeyCode.F7))
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
                Plugin.FailUi("OnGUI phase=" + StudioWindow.Phase, ex);
                try { StudioWindow.EndOpenGroups(); } catch (Exception) { }
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
        static string _status = "The editor is closed until you press F7.";
        static GUIStyle _label;
        static GUIStyle _title;
        static GUIStyle _section;
        static GUIStyle _button;
        static GUIStyle _primary;
        static GUIStyle _well;
        static int _styleSize;
        static string _focus;
        static bool _awaiting;
        static int _seenGeneration;
        static string _replyIntent = "";
        static string _menu = "";
        static bool _showAllStats;
        static readonly StudioSession _session = new StudioSession();
        static readonly Dictionary<string, PromptBuffer> _buffers = new Dictionary<string, PromptBuffer>();
        static readonly List<string> _history = new List<string>();
        static string _lookBody = "";
        static string _lookTint = "";
        static bool _lookPaint;
        static string _savedBody = "";
        static string _savedTint = "";
        static bool _savedPaint;
        static AppearanceFile _presets = new AppearanceFile();
        static readonly string[] _swatches = {
            "#1A3FBF", "#C62828", "#F5C518", "#FFD24A", "#FFFFFF", "#1A1A1A", "#2E7D32", "#6A1B9A",
        };

        public static string Phase = "idle";
        static bool _drawFinished;
        static bool _modifiersWarned;
        static bool _characterWarned;
        public static bool BlockGameplayKeys { get { return _open && _focus != null; } }

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
                _session.Home();
                _focus = null;
                Plugin.Info("Studio editor opened.");
            }
            else
            {
                _focus = null;
                ModelPreview.Dispose();
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
            _focus = null;
            ModelPreview.Dispose();
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
                if (BlockGameplayKeys)
                    ev.Use();
                else
                {
                    RequestToggle();
                    ev.Use();
                    return;
                }
            }
            if (!_open)
            {
                ModelPreview.Dispose();
                if (_wasOpen) ReleaseInput();
                _wasOpen = false;
                _focus = null;
                return;
            }
            if (_session.Screen != "look") ModelPreview.Dispose();
            _wasOpen = true;
            Phase = "keys";
            CaptureTyping(ev);
            Phase = "model";
            NoteModel();
            Phase = "styles";
            Styles();
            _area = false;
            _scroll = false;
            Phase = "panel";
            _rect = new Rect(Screen.width * 0.05f, Screen.height * 0.04f, Screen.width * 0.90f, Screen.height * 0.92f);
            Fill(_rect, new Color(0.05f, 0.06f, 0.08f, 1f));
            Phase = "area";
            GUILayout.BeginArea(_rect);
            _area = true;
            try
            {
                Phase = "scroll";
                _scrollPos = GUILayout.BeginScrollView(_scrollPos);
                _scroll = true;
                try
                {
                    Phase = "body";
                    DrawBody();
                    if (!_drawFinished)
                    {
                        _drawFinished = true;
                        Plugin.Info("Studio panel draw finished.");
                    }
                }
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

        static void CaptureTyping(Event ev)
        {
            if (_focus == null || ev == null) return;
            if (ev.type != EventType.KeyDown && ev.type != EventType.KeyUp) return;
            if (ev.type == EventType.KeyDown && ev.keyCode != KeyCode.F7)
            {
                var buf = Buf(_focus);
                if (ev.keyCode == KeyCode.Escape)
                    _focus = null;
                else
                    ApplyKey(buf, ev);
            }
            ev.Use();
        }

        static void ApplyKey(PromptBuffer buf, Event ev)
        {
            Phase = "key";
            bool ctrl = false;
            bool shift = false;
            try
            {
                ctrl = (ev.control || ev.command) && !ev.alt;
                shift = ev.shift;
            }
            catch (Exception ex)
            {
                if (!_modifiersWarned)
                {
                    _modifiersWarned = true;
                    Plugin.Warn("Key modifiers are unavailable: " + ex.GetType().Name);
                }
            }
            var key = ev.keyCode;
            if (ctrl && key == KeyCode.A) { buf.SelectAll(); return; }
            if (ctrl && key == KeyCode.C) { WriteCopy(buf.SelectedText()); return; }
            if (ctrl && key == KeyCode.X)
            {
                var taken = buf.SelectedText();
                if (taken.Length > 0) buf.Insert("");
                WriteCopy(taken);
                return;
            }
            if (ctrl && key == KeyCode.V)
            {
                var pasted = ReadPaste();
                if (pasted != null) buf.Insert(pasted);
                return;
            }
            if (key == KeyCode.Backspace) { buf.Backspace(); return; }
            if (key == KeyCode.Delete) { buf.Delete(); return; }
            if (key == KeyCode.LeftArrow) { buf.Move("left", shift, ctrl); return; }
            if (key == KeyCode.RightArrow) { buf.Move("right", shift, ctrl); return; }
            if (key == KeyCode.UpArrow) { buf.Move("up", shift, ctrl); return; }
            if (key == KeyCode.DownArrow) { buf.Move("down", shift, ctrl); return; }
            if (key == KeyCode.Home) { buf.Move("home", shift, ctrl); return; }
            if (key == KeyCode.End) { buf.Move("end", shift, ctrl); return; }
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter) { buf.Insert("\n"); return; }
            if (ctrl) return;
            Phase = "key-character";
            char ch = '\0';
            try { ch = ev.character; }
            catch (Exception ex)
            {
                if (!_characterWarned)
                {
                    _characterWarned = true;
                    Plugin.Warn("Key character is unavailable: " + ex.GetType().Name);
                }
                return;
            }
            if (ch >= ' ') buf.Insert(ch.ToString());
        }

        static string ReadPaste()
        {
            try { return GUIUtility.systemCopyBuffer ?? ""; }
            catch (Exception)
            {
                _status = "Paste is unavailable on this build.";
                return null;
            }
        }

        static void WriteCopy(string text)
        {
            try { GUIUtility.systemCopyBuffer = text ?? ""; }
            catch (Exception) { _status = "Copy is unavailable on this build."; }
        }

        static void NoteModel()
        {
            if (_seenGeneration == AiClient.Generation) return;
            _seenGeneration = AiClient.Generation;
            if (!_awaiting) return;
            _awaiting = false;
            if (AiClient.Questions != null && AiClient.Questions.Count > 0)
            {
                _session.NoteReply("questions");
                _focus = "answer";
                Buf("answer").Text = "";
                Buf("answer").Caret = 0;
                Buf("answer").Mark = 0;
                return;
            }
            if (AiClient.Pending != null)
            {
                _session.NoteReply("draft");
                _focus = null;
            }
        }

        static void DrawBody()
        {
            Phase = "top";
            DrawTop();
            if (_file == null) Reload();
            var screen = _session.Screen;
            Phase = screen ?? "home";
            if (screen == "create_prompt") DrawCreatePrompt();
            else if (screen == "create_questions") DrawQuestions();
            else if (screen == "create_preview") DrawCreatePreview();
            else if (screen == "refine_offer") DrawRefineOffer();
            else if (screen == "refine_prompt") DrawRefinePrompt();
            else if (screen == "edit_home") DrawEditHome();
            else if (screen == "edit_questions") DrawQuestions();
            else if (screen == "edit_preview") DrawEditPreview();
            else if (screen == "manual") DrawManual();
            else if (screen == "look_pick") DrawLookPick();
            else if (screen == "look") DrawLook();
            else DrawHome();
        }

        static void DrawTop()
        {
            GUILayout.Label("Megabonk Studio", _title);
            GUILayout.Label(_status, _label);
            if (GUILayout.Button("Close", _button))
                RequestToggle();
            if (GUILayout.Button(_history.Count == 0 ? "Undo" : "Undo last change", _button) && _history.Count > 0)
                Undo();
            if (_session.Screen != "home" && GUILayout.Button("Characters", _button))
                GoHome();
        }

        static void DrawHome()
        {
            GUILayout.Label("Start here", _section);
            GUILayout.Label("Create a character from a description you type. That adds someone new. It does not change the people already in the list.", _label);
            if (Wide("Create a character", new Color(0.55f, 0.86f, 0.70f, 1f)))
                BeginCreate();
            if (Wide("Appearance", new Color(0.98f, 0.62f, 0.18f, 1f)))
                OpenLookPick();
            if (AiClient.Pending != null && _replyIntent == "create" && Wide("Review the new-character draft", new Color(0.55f, 0.86f, 0.70f, 1f)))
            {
                _session.Intent = "create";
                _session.HasEditId = false;
                _session.NoteReply("draft");
            }
            if (AiClient.Pending != null && _replyIntent != "create" && Find(_session.EditId) != null && Wide("Review changes to " + EditedName(), new Color(0.95f, 0.78f, 0.45f, 1f)))
            {
                _session.Intent = _replyIntent.Length == 0 ? "edit" : _replyIntent;
                _session.HasEditId = true;
                _session.NoteReply("draft");
            }
            Section("Your characters");
            GUILayout.Label("Edit is separate. It changes that one character and shows the difference before anything is saved.", _label);
            var rows = _file.Characters;
            if (rows.Count == 0)
                GUILayout.Label("No characters in the list yet.", _label);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                GUILayout.Label(row.Name + "   look " + row.Appearance + "   id " + row.Id, _label);
                if (GUILayout.Button("Look " + row.Name, _button))
                    OpenLook(row.Id);
                if (GUILayout.Button("Edit " + row.Name, _button))
                    BeginEdit(row.Id);
            }
            ConnectionLine();
        }

        static void DrawCreatePrompt()
        {
            Steps("Describe", "Answer", "Preview", 0);
            Banner("New character. Nobody already in the list is replaced.", new Color(0.12f, 0.28f, 0.22f, 1f));
            GUILayout.Label("Click the box and type. Enter starts a new line. Arrows move the caret. Esc gives keys back to the game.", _label);
            DrawBuffer("create", 180f);
            if (OfferSend("Send description") )
                SendPrompt(Buf("create"), "");
            ConnectionLine();
        }

        static void DrawQuestions()
        {
            Steps("Describe", "Answer", "Preview", 1);
            var creating = _session.Intent == "create";
            Banner(creating
                ? "The model needs a few answers before it can add someone new."
                : "The model needs a few answers before it changes " + EditedName() + ".",
                creating ? new Color(0.12f, 0.28f, 0.22f, 1f) : new Color(0.28f, 0.20f, 0.10f, 1f));
            foreach (var question in AiClient.Questions)
                GUILayout.Label("Question: " + question, _label);
            GUILayout.Label("Type the answers below.", _label);
            DrawBuffer("answer", 140f);
            if (OfferSend("Send answers"))
                SendPrompt(Buf("answer"), ContextForIntent());
        }

        static void DrawCreatePreview()
        {
            Steps("Describe", "Answer", "Preview", 2);
            Banner("Applying here adds a new character. Existing ids stay as they are.", new Color(0.12f, 0.28f, 0.22f, 1f));
            DrawDraftFacts(AiClient.Pending);
            if (Wide("Apply as a new character", new Color(0.55f, 0.86f, 0.70f, 1f)))
                ApplyAsNew();
            if (GUILayout.Button("Discard draft", _button))
                DiscardDraft();
        }

        static void DrawRefineOffer()
        {
            var created = Find(_session.CreatedId);
            Banner("Added " + (created == null ? "the new character" : created.Name) + ". The other characters were not changed.", new Color(0.12f, 0.28f, 0.22f, 1f));
            GUILayout.Label("You can type another description to change only this new character.", _label);
            if (Wide("Refine with another prompt", new Color(0.55f, 0.86f, 0.70f, 1f)))
            {
                _session.BeginRefine();
                _focus = "refine";
                var buf = Buf("refine");
                buf.Text = "";
                buf.Caret = 0;
                buf.Mark = 0;
            }
        }

        static void DrawRefinePrompt()
        {
            var created = Find(_session.EditId);
            Banner("This changes " + (created == null ? "the new character" : created.Name) + " only. It does not create another character and does not change the others.", new Color(0.12f, 0.28f, 0.22f, 1f));
            DrawBuffer("refine", 160f);
            if (OfferSend("Send refinement"))
                SendPrompt(Buf("refine"), created == null ? "" : JsonSerializer.Serialize(created));
        }

        static void DrawEditHome()
        {
            var row = Find(_session.EditId);
            if (row == null)
            {
                GUILayout.Label("That character is no longer in the list.", _label);
                return;
            }
            Banner("Editing " + row.Name + ", id " + row.Id + ". This does not create a new character.", new Color(0.28f, 0.20f, 0.10f, 1f));
            GUILayout.Label("Look " + row.Appearance + ". Passive " + Blank(row.Passive) + ". Weapon " + Blank(row.Weapon) + ".", _label);
            if (Wide("Describe a change", new Color(0.95f, 0.78f, 0.45f, 1f)))
            {
                _session.Intent = "edit";
                _session.Screen = "refine_prompt";
                _focus = "refine";
                var buf = Buf("refine");
                buf.Text = "";
                buf.Caret = 0;
                buf.Mark = 0;
                AiClient.BeginTopic("Describe what should change about " + row.Name + ". Nothing is sent until you press Send.");
            }
            if (GUILayout.Button("Edit appearance and stats manually", _button))
            {
                _session.BeginManual();
                var name = Buf("name");
                name.Text = row.Name ?? "";
                name.Caret = name.Text.Length;
                name.Mark = name.Caret;
                _menu = "";
            }
        }

        static void DrawEditPreview()
        {
            var row = Find(_session.EditId);
            Banner("These changes apply to " + (row == null ? "the selected id" : row.Name) + " only. Nothing new is created.", new Color(0.28f, 0.20f, 0.10f, 1f));
            if (row != null && AiClient.Pending != null)
            {
                GUILayout.Label("What will change", _section);
                foreach (var line in DraftDiff.Describe(row, AiClient.Pending))
                    GUILayout.Label(line, _label);
            }
            DrawDraftFacts(AiClient.Pending);
            if (Wide("Apply these changes", new Color(0.95f, 0.78f, 0.45f, 1f)))
                ApplyOntoEdited();
            if (GUILayout.Button("Discard draft", _button))
                DiscardDraft();
        }

        static void DrawManual()
        {
            var row = Find(_session.EditId);
            if (row == null)
            {
                GUILayout.Label("That character is no longer in the list.", _label);
                return;
            }
            Banner("Manual edit of " + row.Name + ", id " + row.Id + ".", new Color(0.16f, 0.18f, 0.24f, 1f));
            GUILayout.Label("A passive is one of the 21 existing effects. A weapon is one of the existing weapons. Paint copies the materials and applies one color to the whole body on the next launch. Skin and clothes change together. With paint off, the shared materials stay as they are.", _label);
            GUILayout.Label("Name", _section);
            DrawBuffer("name", 64f);
            Picker("Appearance", row.Appearance, Catalog.Characters, value => row.Appearance = value, false);
            var passiveShown = string.IsNullOrEmpty(row.Passive) ? "(keep cloned passive)" : row.Passive;
            Picker("Passive", passiveShown, WithBlank(Catalog.Passives), value => row.Passive = value, true);
            var weaponShown = string.IsNullOrEmpty(row.Weapon) ? "(keep cloned weapon)" : row.Weapon;
            Picker("Weapon", weaponShown, WithBlank(Catalog.Weapons), value => row.Weapon = value, true);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(row.Paint ? "Painted " + row.Tint : (string.IsNullOrEmpty(row.Tint) ? "Tint: none" : "Tint stored " + row.Tint), _button))
            {
                row.Tint = "";
                row.Paint = false;
            }
            if (GUILayout.Button("Gold", _button)) { row.Tint = "#FFD24A"; row.Paint = true; }
            if (GUILayout.Button("Lilac", _button)) { row.Tint = "#E0C0FF"; row.Paint = true; }
            if (GUILayout.Button("White", _button)) { row.Tint = "#FFFFFF"; row.Paint = true; }
            if (GUILayout.Button("Blue", _button)) { row.Tint = "#1A3FBF"; row.Paint = true; }
            GUILayout.EndHorizontal();
            Section("Stats");
            GUILayout.Label("Addition adds to the additive component. Multiplication affects the multiplicative component. Flat is the third published mode; its formula was not re-measured. Crit chance treats 1 as 100%.", _label);
            if (GUILayout.Button(_showAllStats ? "Hide unchanged stats" : "Show every stat", _button))
                _showAllStats = !_showAllStats;
            if (row.Stats == null) row.Stats = new List<StudioStat>();
            for (int s = 0; s < Catalog.Stats.Length; s++)
            {
                var statName = Catalog.Stats[s];
                StudioStat have = null;
                foreach (var stat in row.Stats)
                    if (stat.Stat == statName) have = stat;
                if (have == null && !_showAllStats) continue;
                GUILayout.Label(have == null ? statName + ": unchanged" : statName + ": " + have.Modify + " " + have.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), _label);
                if (have == null) continue;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Addition", _button)) have.Modify = "Addition";
                if (GUILayout.Button("Multiplication", _button)) have.Modify = "Multiplication";
                if (GUILayout.Button("Flat", _button)) have.Modify = "Flat";
                if (GUILayout.Button("-1", _button)) have.Amount -= 1f;
                if (GUILayout.Button("+1", _button)) have.Amount += 1f;
                if (GUILayout.Button("-0.1", _button)) have.Amount -= 0.1f;
                if (GUILayout.Button("+0.1", _button)) have.Amount += 0.1f;
                if (GUILayout.Button("Remove", _button)) row.Stats.Remove(have);
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button(_menu == "addstat" ? "Close stat list" : "Add a stat", _button))
            {
                _menu = _menu == "addstat" ? "" : "addstat";
                _focus = _menu == "addstat" ? "filter" : null;
            }
            if (_menu == "addstat")
            {
                GUILayout.Label("Type to filter the stat list.", _label);
                DrawBuffer("filter", 52f);
                var filter = Buf("filter").Text ?? "";
                for (int s = 0; s < Catalog.Stats.Length; s++)
                {
                    var statName = Catalog.Stats[s];
                    if (filter.Length > 0 && statName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (GUILayout.Button("Add " + statName, _button))
                    {
                        var exists = false;
                        foreach (var stat in row.Stats) if (stat.Stat == statName) exists = true;
                        if (!exists) row.Stats.Add(new StudioStat { Stat = statName, Modify = "Addition", Amount = 0f });
                        _menu = "";
                        _focus = null;
                    }
                }
            }
            if (Wide("Save manual edits", new Color(0.75f, 0.82f, 0.9f, 1f)))
            {
                var typed = (Buf("name").Text ?? "").Trim();
                if (typed.Length > 0) row.Name = typed;
                Remember();
                Save();
                _status = "Saved manual edits to " + row.Name + ". Undo restores the previous list.";
            }
        }

        static void DrawDraftFacts(StudioCharacter draft)
        {
            if (draft == null)
            {
                GUILayout.Label("No draft is waiting.", _label);
                return;
            }
            GUILayout.Label("Name: " + draft.Name, _label);
            GUILayout.Label("Description: " + draft.Description, _label);
            GUILayout.Label("Look: " + draft.Appearance, _label);
            GUILayout.Label("Passive: " + Blank(draft.Passive), _label);
            GUILayout.Label("Weapon: " + Blank(draft.Weapon), _label);
            GUILayout.Label("Tint: " + (string.IsNullOrEmpty(draft.Tint) ? "(none)" : draft.Tint) + (draft.Paint ? " painted on the whole body" : " stored, materials stay shared"), _label);
            if (draft.Stats == null || draft.Stats.Count == 0)
                GUILayout.Label("Stats: (none)", _label);
            else
            {
                foreach (var stat in draft.Stats)
                    GUILayout.Label("Stat: " + stat.Stat + " " + stat.Modify + " " + stat.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), _label);
            }
        }

        static void DrawBuffer(string id, float height)
        {
            Phase = "buffer";
            var buf = Buf(id);
            var blink = true;
            try { blink = ((int)(Time.realtimeSinceStartup * 2f) % 2) == 0; } catch (Exception) { }
            var shown = buf.Shown(_focus == id, blink);
            if ((buf.Text == null || buf.Text.Length == 0) && _focus != id)
                shown = "Click here and type";
            _well.fixedHeight = height;
            var previous = GUI.color;
            GUI.color = _focus == id ? new Color(0.75f, 0.95f, 0.86f, 1f) : Color.white;
            if (GUILayout.Button(shown, _well))
            {
                _focus = id;
                if (buf.Caret == 0 && buf.Mark == 0 && buf.Text != null && buf.Text.Length > 0)
                {
                    buf.Caret = buf.Text.Length;
                    buf.Mark = buf.Caret;
                }
            }
            GUI.color = previous;
            _well.fixedHeight = 0f;
        }

        static void OpenLookPick()
        {
            _session.Screen = "look_pick";
            _session.Intent = "look";
            _focus = null;
            _status = "Pick whose look to change. Stats, passive, and weapon stay with that character.";
        }

        static void OpenLook(int id)
        {
            var row = Find(id);
            if (row == null) return;
            _session.BeginEdit(id);
            _session.Screen = "look";
            _session.Intent = "look";
            _lookBody = row.Appearance ?? "";
            _lookTint = row.Tint ?? "";
            _lookPaint = row.Paint;
            _savedBody = _lookBody;
            _savedTint = _lookTint;
            _savedPaint = _lookPaint;
            var hex = Buf("hex");
            hex.Text = _lookTint;
            hex.Caret = hex.Text.Length;
            hex.Mark = hex.Caret;
            _focus = null;
            try { _presets = AppearanceBook.Load(AppearanceBook.PathFor(Plugin.JsonPath)); }
            catch (Exception)
            {
                _presets = new AppearanceFile();
                _status = "Saved looks could not be read. The character list was not changed.";
                return;
            }
            _status = "Appearance of " + row.Name + " id " + row.Id + ". Nothing is saved until you apply it.";
        }

        static void DrawLookPick()
        {
            Banner("Appearance. Chunkier controls, one body, one color.", new Color(0.98f, 0.62f, 0.18f, 1f));
            GUILayout.Label("Choose a character. This does not create a cape, emblem, or separate clothing colors.", _label);
            if (_file.Characters.Count == 0)
                GUILayout.Label("Create a character first. There is nobody to restyle.", _label);
            for (int i = 0; i < _file.Characters.Count; i++)
            {
                var row = _file.Characters[i];
                if (GUILayout.Button("Look " + row.Name + "   " + row.Appearance, _primary))
                    OpenLook(row.Id);
            }
        }

        static void DrawLook()
        {
            var row = Find(_session.EditId);
            if (row == null)
            {
                GUILayout.Label("That character is no longer in the list.", _label);
                return;
            }
            Banner(row.Name + " id " + row.Id + ". Stats, passive, and weapon stay.", new Color(0.98f, 0.62f, 0.18f, 1f));
            DrawPreview();
            GUILayout.Label(_lookPaint
                ? "Body " + _lookBody + ". Paint on. One color for the whole body, including skin."
                : "Body " + _lookBody + ". Paint off. The shared materials stay as they are.", _label);
            Section("Body");
            for (int i = 0; i < Catalog.Characters.Length; i++)
            {
                var body = Catalog.Characters[i];
                var mark = body == _lookBody ? "Selected body: " : "Body: ";
                if (GUILayout.Button(mark + body, body == _lookBody ? _primary : _button))
                    _lookBody = body;
            }
            Section("Color");
            for (int i = 0; i < _swatches.Length; i++)
            {
                if (SwatchButton(_swatches[i]))
                {
                    _lookTint = _swatches[i];
                    _lookPaint = true;
                    Buf("hex").Text = _lookTint;
                    Buf("hex").Caret = _lookTint.Length;
                    Buf("hex").Mark = _lookTint.Length;
                }
            }
            GUILayout.Label("Type a hex color. Six digits, with or without #.", _label);
            DrawBuffer("hex", 64f);
            if (GUILayout.Button("Turn left", _button)) ModelPreview.Turn(-30f);
            if (GUILayout.Button("Turn right", _button)) ModelPreview.Turn(30f);
            if (GUILayout.Button("Use typed color", _button))
            {
                var hex = AppearanceBook.NormalizeHex(Buf("hex").Text);
                if (hex == null) _status = "That is not a hex color. The look was not changed.";
                else
                {
                    _lookTint = hex;
                    _lookPaint = true;
                    _status = "Typed color " + hex + " is in the preview. Apply to keep it.";
                }
            }
            if (GUILayout.Button(_lookPaint ? "Paint off" : "Paint on", _button))
            {
                if (_lookPaint) _lookPaint = false;
                else if (AppearanceBook.Hex(_lookTint)) _lookPaint = true;
                else _status = "Pick a color before turning paint on.";
            }
            if (GUILayout.Button("Reset look", _button))
            {
                _lookBody = _savedBody;
                _lookTint = _savedTint;
                _lookPaint = _savedPaint;
                Buf("hex").Text = _lookTint;
                _status = "Restored the look that is already saved for " + row.Name + ".";
            }
            Section("Saved looks");
            GUILayout.Label("Name this look, then save it. Saving does not change the character.", _label);
            DrawBuffer("presetname", 64f);
            if (GUILayout.Button("Save preset", _button))
                SavePreset();
            if (_presets.Presets != null)
            {
                for (int i = 0; i < _presets.Presets.Count; i++)
                {
                    var preset = _presets.Presets[i];
                    if (preset == null) continue;
                    if (GUILayout.Button("Use " + preset.Name + "   " + preset.Appearance, _button))
                    {
                        _lookBody = preset.Appearance;
                        _lookTint = preset.Tint ?? "";
                        _lookPaint = preset.Paint;
                        Buf("hex").Text = _lookTint;
                        _status = "Loaded " + preset.Name + " into the preview. The character is unchanged until you apply.";
                    }
                }
            }
            if (Wide("Apply to " + row.Name, new Color(0.98f, 0.62f, 0.18f, 1f)))
                ApplyLook(row, false);
            if (Wide("Save as a new character", new Color(0.55f, 0.86f, 0.70f, 1f)))
                ApplyLook(row, true);
        }

        static bool _previewNoted;

        static void DrawPreview()
        {
            Phase = "preview";
            try
            {
                ModelPreview.Ensure(_lookBody, _lookTint, _lookPaint);
                float side = Mathf.Clamp(_rect.width * 0.42f, 180f, 420f);
                ModelPreview.Draw(new Rect(8f, 8f, side, side));
                GUILayout.Label("Isolated preview of " + _lookBody + ". It is not saved until you apply. Not part of the level.", _label);
            }
            catch (Exception ex)
            {
                ModelPreview.Dispose();
                Plugin.WarnOnce("preview", ex);
                DrawSwatch();
            }
        }

        static void DrawSwatch()
        {
            var previous = GUI.color;
            var caption = "Whole-body color. Not a 3D model.";
            if (!_lookPaint)
            {
                GUI.color = new Color(0.75f, 0.75f, 0.75f, 1f);
                caption = "Paint off. Shared materials. Not a 3D model.";
            }
            else if (TryLookColor(_lookTint, out var color))
                GUI.color = color;
            else
            {
                GUI.color = Color.white;
                caption = "Color is not ready. Not a 3D model.";
            }
            GUILayout.Button(" ", _primary);
            GUI.color = previous;
            GUILayout.Label(caption + "  " + (_lookTint.Length == 0 ? "(no tint)" : _lookTint), _label);
        }

        static bool SwatchButton(string tint)
        {
            return GUILayout.Button(tint == _lookTint && _lookPaint ? "Selected " + tint : tint, _button);
        }

        static bool TryLookColor(string tint, out Color color)
        {
            color = Color.white;
            if (!AppearanceBook.Hex(tint)) return false;
            int r = Convert.ToInt32(tint.Substring(1, 2), 16);
            int g = Convert.ToInt32(tint.Substring(3, 2), 16);
            int b = Convert.ToInt32(tint.Substring(5, 2), 16);
            color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return true;
        }

        static void SavePreset()
        {
            var preset = new AppearancePreset
            {
                Name = (Buf("presetname").Text ?? "").Trim(),
                Appearance = _lookBody,
                Tint = _lookTint ?? "",
                Paint = _lookPaint,
            };
            var problem = AppearanceBook.Problem(preset, _presets);
            if (problem != null)
            {
                _status = problem + " The character list was not changed.";
                return;
            }
            preset.Name = preset.Name.Trim();
            try
            {
                if (_presets.Presets == null) _presets.Presets = new List<AppearancePreset>();
                _presets.Presets.Add(preset);
                AppearanceBook.Save(AppearanceBook.PathFor(Plugin.JsonPath), _presets);
                _status = "Saved the look " + preset.Name + ". " + EditedName() + " was not changed.";
            }
            catch (Exception)
            {
                _presets.Presets.Remove(preset);
                _status = "The look could not be saved. The character list was not changed.";
            }
        }

        static void ApplyLook(StudioCharacter row, bool asNew)
        {
            if (Catalog.IndexOf(Catalog.Characters, _lookBody) < 0)
            {
                _status = "That body is not in the catalog. Nothing was written.";
                return;
            }
            if (_lookPaint && !AppearanceBook.Hex(_lookTint))
            {
                _status = "Paint needs a hex color. Nothing was written.";
                return;
            }
            Remember();
            if (!asNew)
            {
                row.Appearance = _lookBody;
                row.Tint = _lookTint ?? "";
                row.Paint = _lookPaint;
                Save();
                _savedBody = row.Appearance;
                _savedTint = row.Tint;
                _savedPaint = row.Paint;
                _status = "Updated the look of " + row.Name + " id " + row.Id + ". Stats, passive, and weapon are unchanged. Paint shows on the next launch.";
                return;
            }
            var made = new StudioCharacter
            {
                Id = NextId(),
                Name = row.Name,
                Description = row.Description,
                Author = row.Author,
                Appearance = _lookBody,
                Tint = _lookTint ?? "",
                Paint = _lookPaint,
                Passive = row.Passive,
                Weapon = row.Weapon,
                Portrait = row.Portrait,
                Stats = new List<StudioStat>(),
            };
            if (row.Stats != null)
            {
                for (int i = 0; i < row.Stats.Count; i++)
                {
                    var stat = row.Stats[i];
                    made.Stats.Add(new StudioStat { Stat = stat.Stat, Modify = stat.Modify, Amount = stat.Amount });
                }
            }
            _file.Characters.Add(made);
            Save();
            _status = "Added id " + made.Id + " with this look. " + row.Name + " id " + row.Id + " was not changed.";
        }

        static int NextId()
        {
            if (_file.Characters.Count == 0) return 1842201100;
            int ident = 1000;
            for (int i = 0; i < _file.Characters.Count; i++)
                if (_file.Characters[i].Id > ident) ident = _file.Characters[i].Id;
            return ident + 1;
        }

        static void BeginCreate()
        {
            _session.BeginCreate();
            _replyIntent = "create";
            var buf = Buf("create");
            buf.Text = "";
            buf.Caret = 0;
            buf.Mark = 0;
            _focus = "create";
            AiClient.BeginTopic("Type a description. Nothing is sent until you press Send.");
            _status = AiClient.Status;
        }

        static void BeginEdit(int id)
        {
            _session.BeginEdit(id);
            _focus = null;
            _replyIntent = "edit";
            _status = "Editing " + EditedName() + ". Creating a new character is the other button, on the character list.";
        }

        static void GoHome()
        {
            _session.Home();
            _focus = null;
            _menu = "";
        }

        static void SendPrompt(PromptBuffer buf, string context)
        {
            var text = (buf.Text ?? "").Trim();
            if (text.Length == 0)
            {
                _status = "Type a prompt first. Nothing was sent.";
                return;
            }
            _replyIntent = _session.Intent;
            _awaiting = true;
            AiClient.Ask(text, context ?? "");
            _status = AiClient.Status;
        }

        static string ContextForIntent()
        {
            if (_session.Intent == "create") return "";
            var row = Find(_session.EditId);
            return row == null ? "" : JsonSerializer.Serialize(row);
        }

        static void ApplyAsNew()
        {
            if (!_session.TryApplyNew(out _))
            {
                _status = "This screen does not add a character.";
                return;
            }
            int before = _file == null || _file.Characters == null ? 0 : _file.Characters.Count;
            ApplyPending(null);
            if (_file == null || _file.Characters == null || _file.Characters.Count == before) return;
            var created = _file.Characters[_file.Characters.Count - 1];
            _session.NoteCreated(created.Id);
            _focus = null;
            _status = "Added " + created.Name + " as a new character. Existing characters were not replaced.";
        }

        static void ApplyOntoEdited()
        {
            int id;
            if (!_session.TryApplyEdit(out id))
            {
                _status = "This screen does not change an existing character.";
                return;
            }
            var row = Find(id);
            if (row == null)
            {
                _status = "That character is not in the list. Nothing was written.";
                return;
            }
            ApplyPending(row);
            _session.BeginEdit(id);
            _focus = null;
            _status = "Updated " + row.Name + " and kept id " + id + ". Undo restores the previous list.";
        }

        static void DiscardDraft()
        {
            AiClient.ClearPending();
            _focus = null;
            GoHome();
            _status = "Draft discarded. The character list was not changed.";
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
        }

        static PromptBuffer Buf(string id)
        {
            PromptBuffer buf;
            if (!_buffers.TryGetValue(id, out buf))
            {
                buf = new PromptBuffer();
                _buffers[id] = buf;
            }
            return buf;
        }

        static StudioCharacter Find(int id)
        {
            if (_file == null || _file.Characters == null) return null;
            for (int i = 0; i < _file.Characters.Count; i++)
                if (_file.Characters[i].Id == id) return _file.Characters[i];
            return null;
        }

        static string EditedName()
        {
            var row = Find(_session.EditId);
            return row == null ? "the selected character" : row.Name;
        }

        static string Blank(string value)
        {
            return string.IsNullOrEmpty(value) ? "(cloned)" : value;
        }

        static void Steps(string a, string b, string c, int current)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Step(a, current == 0), _label);
            GUILayout.Label(Step(b, current == 1), _label);
            GUILayout.Label(Step(c, current == 2), _label);
            GUILayout.EndHorizontal();
        }

        static string Step(string name, bool on)
        {
            return on ? "● " + name : "○ " + name;
        }

        static void Banner(string text, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, _section);
            GUI.color = previous;
        }

        static void Section(string title)
        {
            GUILayout.Label(title, _section);
        }

        static bool OfferSend(string label)
        {
            int seconds;
            if (AiClient.SendPaused(out seconds))
            {
                GUILayout.Label("Send is paused for " + seconds + " seconds. Your prompt is still here.", _label);
                return false;
            }
            return Wide(AiClient.Busy ? "Waiting for the model" : label, new Color(0.55f, 0.86f, 0.70f, 1f)) && !AiClient.Busy;
        }

        static bool Wide(string text, Color color)
        {
            Phase = "wide-button";
            var previous = GUI.color;
            GUI.color = color;
            var hit = GUILayout.Button(text, _primary);
            GUI.color = previous;
            return hit;
        }

        static void ConnectionLine()
        {
            Phase = "connection";
            if (!AiClient.TryConfig(out var baseUrl, out var model, out _, out _))
                GUILayout.Label("No model is connected. Nothing is sent until ai.json beside characters.json has base_url and model. The key stays in MEGABONK_AI_KEY in this game process.", _label);
            else
                GUILayout.Label(AiClient.Status + "  Server " + baseUrl + ", model " + model + ".", _label);
        }

        static void Picker(string label, string current, string[] values, Action<string> set, bool blankLabel)
        {
            var open = _menu == label;
            if (GUILayout.Button(label + ":  " + current + (open ? "    (close list)" : "    (open list)"), _button))
            {
                _menu = open ? "" : label;
                _focus = _menu.Length == 0 ? null : "filter";
                Buf("filter").Text = "";
                Buf("filter").Caret = 0;
                Buf("filter").Mark = 0;
            }
            if (!open) return;
            GUILayout.Label("Type to narrow this list.", _label);
            DrawBuffer("filter", 52f);
            var filter = Buf("filter").Text ?? "";
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                var shown = value.Length == 0 ? "(keep cloned)" : value;
                if (filter.Length > 0 && shown.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button(shown, _button))
                {
                    set(blankLabel && value.Length == 0 ? "" : value);
                    _menu = "";
                    _focus = null;
                }
            }
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
            GoHome();
            _status = "Restored the previous character list.";
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
            }
            catch (Exception ex)
            {
                _status = "Save failed: " + ex.Message;
                Plugin.Warn(_status);
            }
        }

        static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static void Styles()
        {
            int size = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 34f), 20, 36);
            if (_label != null && _styleSize == size) return;
            _styleSize = size;
            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = size;
            _label.wordWrap = true;
            _label.normal.textColor = Color.white;
            _title = new GUIStyle(_label);
            _title.fontSize = Mathf.Clamp(size + 10, 28, 48);
            _section = new GUIStyle(_label);
            _section.fontSize = size + 2;
            _well = MakeButton(size, size * 4f);
            _well.wordWrap = true;
            _button = MakeButton(size, size * 1.8f);
            _primary = MakeButton(size + 2, size * 2.8f);
        }

        static GUIStyle MakeButton(int font, float height)
        {
            var style = new GUIStyle(GUI.skin.button);
            style.fontSize = font;
            style.fixedHeight = height;
            style.wordWrap = true;
            var border = new RectOffset();
            border.left = 0;
            border.right = 0;
            border.top = 0;
            border.bottom = 0;
            style.border = border;
            Paint(style.normal);
            Paint(style.hover);
            Paint(style.active);
            Paint(style.focused);
            Paint(style.onNormal);
            Paint(style.onHover);
            Paint(style.onActive);
            Paint(style.onFocused);
            return style;
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
