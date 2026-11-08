using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Assets.Scripts._Data.Hats;
using Assets.Scripts.Inventory__Items__Pickups.AbilitiesPassive;
using Assets.Scripts.Inventory__Items__Pickups.Stats;
using Assets.Scripts.Menu.Shop;
using Assets.Scripts.Saves___Serialization.Progression.Achievements;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
// A using alias ignores other usings in this file, so StatModifier must be qualified.
// Metadata: Assets.Scripts.Inventory__Items__Pickups.Stats.StatModifier.
using Il2CppList = Il2CppSystem.Collections.Generic.List<Assets.Scripts.Inventory__Items__Pickups.Stats.StatModifier>;

namespace UmMegabonkStudio
{
    // Clone path used by the shipped Guz character mod on this game: Instantiate the vanilla
    // CharacterData, point eCharacter at a free id, copy one skin, and assign an existing
    // passive and weapon by reference. Stat extras go on a new list so the vanilla list is
    // left alone. This class is not executed until the game loads the compiled plugin.
    sealed class Live
    {
        public CharacterData Data;
        public SkinData Skin;
        public ECharacter Appearance;
        public readonly List<StatModifier> BaseStats = new List<StatModifier>();
    }

    static class Registrar
    {
        public static readonly Dictionary<int, Live> ById = new Dictionary<int, Live>();
        public static readonly Dictionary<IntPtr, string> Names = new Dictionary<IntPtr, string>();
        public static readonly Dictionary<IntPtr, string> Descriptions = new Dictionary<IntPtr, string>();
        public static DataManager LastManager;
        static bool _portraitWarned;

        public static int RegisterAll(DataManager dm, string jsonPath)
        {
            LastManager = dm;
            if (dm == null || dm.characterData == null || dm.unsortedCharacterData == null || dm.characterData.Count == 0)
            {
                Plugin.Warn("DataManager has no characters yet. Nothing was registered.");
                return 0;
            }
            var file = Drafts.Load(jsonPath);
            int n = 0;
            foreach (var draft in file.Characters)
            {
                try
                {
                    if (Register(dm, jsonPath, draft)) n++;
                }
                catch (Exception ex)
                {
                    Plugin.Warn("Skipped '" + (draft == null ? "?" : draft.Name) + "': " + ex.Message);
                }
            }
            return n;
        }

        public static bool Register(DataManager dm, string jsonPath, StudioCharacter draft)
        {
            if (draft == null) return false;
            var id = (ECharacter)draft.Id;
            if (dm.characterData.ContainsKey(id) || ById.ContainsKey(draft.Id))
                return false;
            if (!TryParse(typeof(ECharacter), draft.Appearance, out var appearanceRaw))
            {
                Plugin.Warn("'" + draft.Name + "' appearance '" + draft.Appearance + "' is not an ECharacter name.");
                return false;
            }
            var appearance = (ECharacter)appearanceRaw;
            if (!dm.characterData.ContainsKey(appearance))
            {
                Plugin.Warn("'" + draft.Name + "' cannot clone " + draft.Appearance + " because it is not in characterData.");
                return false;
            }
            var source = dm.characterData[appearance];
            if (source == null)
            {
                Plugin.Warn("'" + draft.Name + "' source CharacterData is null.");
                return false;
            }
            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = draft.Name;
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            clone.eCharacter = id;
            var unlock = (UnlockableBase)clone;
            unlock.author = string.IsNullOrWhiteSpace(draft.Author) ? "local" : draft.Author;
            unlock.isEnabled = true;
            unlock.showInUnlocks = false;
            unlock.canAlwaysToggle = false;
            unlock.price = 0;
            unlock.sortingPriority = -100000;
            clone.achievementRequirement = null;
            clone.numQuestsRequiredForVisibilityInCharacterSelection = 0;
            // CharacterData has no description field. GetDescription() returns the string;
            // Hooks.DescriptionPrefix supplies draft.Description for this object.

            var live = new Live { Data = clone, Appearance = appearance };
            if (clone.statModifiers != null)
            {
                for (int i = 0; i < clone.statModifiers.Count; i++)
                    live.BaseStats.Add(clone.statModifiers[i]);
            }
            WriteStats(clone, live, draft);
            AssignPassive(dm, clone, source, draft.Passive);
            AssignWeapon(dm, clone, source, draft.Weapon);
            live.Skin = CopySkin(dm, appearance, id, draft);
            var portrait = TryPortrait(jsonPath, draft.Portrait);
            if (portrait != null) clone.icon = (Texture)portrait;
            try { clone.Init(); }
            catch (Exception ex) { Plugin.Warn("'" + draft.Name + "' Init() threw: " + ex.Message); }

            Remember(clone, draft.Name, draft.Description ?? "");
            if (live.Skin != null) Remember(live.Skin, draft.Name, draft.Description ?? "");
            dm.characterData[id] = clone;
            dm.unsortedCharacterData.Add(clone);
            if (dm.skinData != null && live.Skin != null)
            {
                var skins = new Il2CppSystem.Collections.Generic.List<SkinData>();
                skins.Add(live.Skin);
                dm.skinData[id] = skins;
            }
            try { CopyHats(dm, appearance, id); }
            catch (Exception ex) { Plugin.Warn("Hat placements for '" + draft.Name + "' were not copied: " + ex.Message); }
            ById[draft.Id] = live;
            Plugin.Info("Registered '" + draft.Name + "' id " + draft.Id + " cloned from " + draft.Appearance
                + ", passive " + (string.IsNullOrEmpty(draft.Passive) ? "(cloned)" : draft.Passive)
                + ", weapon " + (string.IsNullOrEmpty(draft.Weapon) ? "(cloned)" : draft.Weapon) + ".");
            return true;
        }

        public static void ApplyLive(DataManager dm, string jsonPath, StudioCharacter draft)
        {
            if (draft == null) return;
            if (!ById.TryGetValue(draft.Id, out var live) || live.Data == null)
            {
                Register(dm, jsonPath, draft);
                return;
            }
            int appearance = Catalog.IndexOf(Catalog.Characters, draft.Appearance);
            if (appearance >= 0 && (ECharacter)appearance != live.Appearance)
                Plugin.Warn("'" + draft.Name + "' appearance change to " + draft.Appearance + " applies on the next launch.");
            Remember(live.Data, draft.Name, draft.Description ?? "");
            if (live.Skin != null) Remember(live.Skin, draft.Name, draft.Description ?? "");
            WriteStats(live.Data, live, draft);
            AssignPassive(dm, live.Data, dm.characterData[live.Appearance], draft.Passive);
            AssignWeapon(dm, live.Data, dm.characterData[live.Appearance], draft.Weapon);
            if (live.Skin != null && TryTint(draft.Tint, out var tint))
                TintMaterials(live.Skin, tint);
            Plugin.Info("Updated '" + draft.Name + "' for the next run. A new appearance still needs a restart.");
        }

        static void WriteStats(CharacterData data, Live live, StudioCharacter draft)
        {
            var copy = new Il2CppList();
            for (int i = 0; i < live.BaseStats.Count; i++)
                copy.Add(live.BaseStats[i]);
            if (draft.Stats == null) { data.statModifiers = copy; return; }
            foreach (var extra in draft.Stats)
            {
                int si = Catalog.IndexOf(Catalog.Stats, extra.Stat);
                int mi = Catalog.IndexOf(Catalog.ModifyTypes, extra.Modify);
                if (si < 0 || mi < 0)
                {
                    Plugin.Warn("Ignored unknown stat '" + extra.Stat + "' / '" + extra.Modify + "'.");
                    continue;
                }
                var mod = new StatModifier();
                mod.stat = (EStat)si;
                mod.modifyType = (EStatModifyType)mi;
                mod.modification = extra.Amount;
                copy.Add(mod);
            }
            data.statModifiers = copy;
        }

        static void AssignPassive(DataManager dm, CharacterData clone, CharacterData source, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                clone.passive = source.passive;
                return;
            }
            if (!TryParse(typeof(EPassive), name, out var raw))
            {
                Plugin.Warn("Passive '" + name + "' is not an EPassive name. Keeping the cloned passive.");
                clone.passive = source.passive;
                return;
            }
            var want = (EPassive)raw;
            var list = dm.unsortedCharacterData;
            for (int i = 0; i < list.Count; i++)
            {
                var row = list[i];
                if (row == null || row.passive == null || row.passive.ePassive != want) continue;
                var copy = UnityEngine.Object.Instantiate(row.passive);
                copy.name = row.passive.name;
                copy.hideFlags = HideFlags.DontUnloadUnusedAsset;
                clone.passive = copy;
                return;
            }
            Plugin.Warn("No character has passive " + name + ". Keeping the cloned passive.");
            clone.passive = source.passive;
        }

        static void AssignWeapon(DataManager dm, CharacterData clone, CharacterData source, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                clone.weapon = source.weapon;
                return;
            }
            if (!TryParse(typeof(EWeapon), name, out var raw))
            {
                Plugin.Warn("Weapon '" + name + "' is not an EWeapon name. Keeping the cloned weapon.");
                clone.weapon = source.weapon;
                return;
            }
            var want = (EWeapon)raw;
            if (dm.weapons != null && dm.weapons.ContainsKey(want))
            {
                clone.weapon = dm.weapons[want];
                return;
            }
            Plugin.Warn("Weapon " + name + " is not in DataManager.weapons. Keeping the cloned weapon.");
            clone.weapon = source.weapon;
        }

        static SkinData CopySkin(DataManager dm, ECharacter appearance, ECharacter id, StudioCharacter draft)
        {
            if (dm.skinData == null || !dm.skinData.ContainsKey(appearance))
            {
                Plugin.Warn("'" + draft.Name + "' has no skinData for " + draft.Appearance + ". The model may be invisible.");
                return null;
            }
            var skins = dm.skinData[appearance];
            SkinData source = null;
            for (int i = 0; i < skins.Count; i++)
            {
                if (skins[i] != null) { source = skins[i]; break; }
            }
            if (source == null) return null;
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = draft.Name + " skin";
            copy.hideFlags = HideFlags.DontUnloadUnusedAsset;
            copy.character = id;
            var unlock = (UnlockableBase)copy;
            unlock.author = string.IsNullOrWhiteSpace(draft.Author) ? "local" : draft.Author;
            unlock.price = 0;
            unlock.isEnabled = true;
            if (TryTint(draft.Tint, out var tint)) TintMaterials(copy, tint);
            return copy;
        }

        static void TintMaterials(SkinData skin, Color tint)
        {
            var mats = skin.materials;
            if (mats == null) return;
            var next = new Il2CppReferenceArray<Material>(mats.Length);
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                if (src == null) continue;
                var made = new Material(src);
                made.name = src.name + " (studio)";
                made.hideFlags = HideFlags.DontUnloadUnusedAsset;
                TintProperty(made, "_Color", tint);
                TintProperty(made, "_AlbedoColor", tint);
                next[i] = made;
            }
            skin.materials = next;
        }

        static void TintProperty(Material mat, string property, Color tint)
        {
            if (!mat.HasProperty(property)) return;
            var color = mat.GetColor(property);
            mat.SetColor(property, new Color(color.r * tint.r, color.g * tint.g, color.b * tint.b, color.a));
        }

        static void CopyHats(DataManager dm, ECharacter appearance, ECharacter id)
        {
            var hats = dm.unsortedHats;
            if (hats == null) return;
            for (int i = 0; i < hats.Count; i++)
            {
                var hat = hats[i];
                if (hat == null || hat.orientations == null) continue;
                HatOrientation from = null;
                bool already = false;
                for (int j = 0; j < hat.orientations.Count; j++)
                {
                    var row = hat.orientations[j];
                    if (row == null) continue;
                    if (row.character == id) already = true;
                    else if (row.character == appearance) from = row;
                }
                if (already || from == null) continue;
                var neu = new HatOrientation();
                neu.character = id;
                neu.meshForHat = from.meshForHat;
                neu.pos = from.pos;
                neu.rot = from.rot;
                neu.scale = from.scale;
                hat.orientations.Add(neu);
            }
        }

        static Texture2D TryPortrait(string jsonPath, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return null;
            try
            {
                var full = Path.Combine(Path.GetDirectoryName(jsonPath) ?? "", relative);
                if (!File.Exists(full))
                {
                    Plugin.Warn("Portrait not found: " + relative + ". Using the cloned icon.");
                    return null;
                }
                var bytes = File.ReadAllBytes(full);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                var asm = AppDomain.CurrentDomain.GetAssemblies();
                Type conv = null;
                for (int i = 0; i < asm.Length && conv == null; i++)
                    conv = asm[i].GetType("UnityEngine.ImageConversion");
                var method = conv == null ? null : conv.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                if (method == null)
                {
                    if (!_portraitWarned)
                    {
                        _portraitWarned = true;
                        Plugin.Warn("ImageConversion.LoadImage is not available. Portraits stay on the cloned icon.");
                    }
                    return null;
                }
                var ok = method.Invoke(null, new object[] { tex, bytes });
                if (ok is bool flag && !flag) return null;
                return tex;
            }
            catch (Exception ex)
            {
                if (!_portraitWarned)
                {
                    _portraitWarned = true;
                    Plugin.Warn("Portrait load failed, using the cloned icon: " + ex.Message);
                }
                return null;
            }
        }

        static void Remember(UnityEngine.Object obj, string name, string description)
        {
            var ptr = ((Il2CppObjectBase)(object)obj).Pointer;
            Names[ptr] = name ?? "";
            Descriptions[ptr] = description ?? "";
        }

        public static bool TryText(UnlockableBase obj, bool description, out string text)
        {
            text = null;
            if (obj == null) return false;
            var ptr = ((Il2CppObjectBase)(object)obj).Pointer;
            var map = description ? Descriptions : Names;
            return map.TryGetValue(ptr, out text);
        }

        static bool TryParse(Type enumType, string name, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var parsed = Enum.Parse(enumType, name.Trim(), true);
                if (!Enum.IsDefined(enumType, parsed)) return false;
                value = (int)parsed;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        static bool TryTint(string hex, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            hex = hex.Trim();
            if (hex.Length != 7 || hex[0] != '#') return false;
            if (!byte.TryParse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return false;
            if (!byte.TryParse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return false;
            if (!byte.TryParse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
            color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return true;
        }
    }
}
