using System;
using System.Collections.Generic;
using UnityEngine;

namespace UmMegabonkStudio
{
    // Isolated look at a vanilla body. The camera stays disabled and renders only into a texture.
    // Nothing here writes characters.json, skin records, or the shared material array.
    static class ModelPreview
    {
        const float Anchor = 250000f;
        static GameObject _root;
        static GameObject _camGo;
        static Camera _cam;
        static RenderTexture _rt;
        static readonly List<Material> _owned = new List<Material>();
        static string _body = "";
        static string _tint = "";
        static bool _paint;
        static float _yaw;

        public static bool Alive;

        public static bool Matches(string body, string tint, bool paint)
        {
            return Alive && _body == (body ?? "") && _tint == (tint ?? "") && _paint == paint;
        }

        public static void Ensure(string body, string tint, bool paint)
        {
            body = body ?? "";
            tint = tint ?? "";
            if (Matches(body, tint, paint)) return;
            Dispose();
            var dm = Registrar.LastManager;
            if (dm == null || dm.characterData == null)
                throw new InvalidOperationException("characters are not loaded");
            int index = Catalog.IndexOf(Catalog.Characters, body);
            if (index < 0) throw new InvalidOperationException("body is not in the catalog");
            var id = (ECharacter)index;
            if (!dm.characterData.ContainsKey(id) || dm.characterData[id] == null)
                throw new InvalidOperationException("body is not in characterData");
            var data = dm.characterData[id];
            _rt = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32);
            _rt.name = "UmMegabonkPreview";
            _rt.hideFlags = HideFlags.HideAndDontSave;
            if (!_rt.Create()) throw new InvalidOperationException("render texture was not created");
            _camGo = new GameObject("UmMegabonkPreviewCamera");
            _camGo.hideFlags = HideFlags.HideAndDontSave;
            _cam = _camGo.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.07f, 0.08f, 0.1f, 1f);
            _cam.orthographic = true;
            _cam.farClipPlane = 40f;
            _cam.cullingMask = -1;
            Silence(_camGo);
            if (data.prefab != null)
            {
                _root = UnityEngine.Object.Instantiate(data.prefab);
                _root.name = "UmMegabonkPreviewBody";
            }
            else if (data.meshDefault != null)
            {
                _root = new GameObject("UmMegabonkPreviewBody");
                var filter = _root.AddComponent<MeshFilter>();
                filter.sharedMesh = data.meshDefault;
                _root.AddComponent<MeshRenderer>();
            }
            else throw new InvalidOperationException("body has no prefab or mesh");
            _root.hideFlags = HideFlags.HideAndDontSave;
            _root.SetActive(false);
            _root.transform.position = new Vector3(Anchor, Anchor, Anchor);
            _root.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            Silence(_root);
            PaintRenderers(_root, tint, paint);
            _root.SetActive(true);
            Frame();
            _cam.enabled = false;
            _body = body;
            _tint = tint;
            _paint = paint;
            Alive = true;
        }

        public static void Turn(float degrees)
        {
            _yaw += degrees;
            if (_root == null) return;
            _root.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            Frame();
        }

        public static void Draw(Rect rect)
        {
            if (!Alive || _cam == null || _rt == null) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            _cam.enabled = false;
            _cam.Render();
            _cam.enabled = false;
            GUI.DrawTexture(rect, _rt);
        }

        public static void Dispose()
        {
            Alive = false;
            _body = "";
            _tint = "";
            _paint = false;
            if (_cam != null)
            {
                _cam.targetTexture = null;
                _cam.enabled = false;
            }
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
                _rt = null;
            }
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i] != null) UnityEngine.Object.Destroy(_owned[i]);
            }
            _owned.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_camGo != null) UnityEngine.Object.Destroy(_camGo);
            _root = null;
            _camGo = null;
            _cam = null;
        }

        static void Frame()
        {
            if (_cam == null || _root == null) return;
            var renderers = _root.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            Bounds bounds = new Bounds(_root.transform.position, Vector3.one);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (!any) bounds = renderers[i].bounds;
                else bounds.Encapsulate(renderers[i].bounds);
                any = true;
            }
            float radius = 1f;
            if (any)
            {
                radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
                if (radius < 0.25f) radius = 0.25f;
            }
            else bounds = new Bounds(_root.transform.position + Vector3.up, Vector3.one);
            _cam.orthographic = true;
            _cam.orthographicSize = radius * 1.35f;
            _cam.farClipPlane = radius * 10f + 8f;
            var focus = bounds.center;
            _cam.transform.position = focus + new Vector3(0f, radius * 0.2f, radius * 3.2f);
            _cam.transform.rotation = Quaternion.LookRotation(focus - _cam.transform.position);
            _cam.enabled = false;
        }

        static void PaintRenderers(GameObject root, string tint, bool paint)
        {
            Color color = Color.white;
            bool colored = paint && Hex(tint, out color);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null) continue;
                var shared = renderer.sharedMaterials;
                if (shared == null) continue;
                var copies = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] == null) continue;
                    var inst = new Material(shared[i]);
                    inst.hideFlags = HideFlags.HideAndDontSave;
                    inst.name = shared[i].name + " preview";
                    if (colored) Paint(inst, color);
                    copies[i] = inst;
                    _owned.Add(inst);
                }
                renderer.materials = copies;
            }
        }

        static void Paint(Material mat, Color color)
        {
            string[] names = { "_Color", "_BaseColor", "_AlbedoColor" };
            for (int i = 0; i < names.Length; i++)
            {
                if (!mat.HasProperty(names[i])) continue;
                mat.SetColor(names[i], color);
            }
        }

        static void Silence(GameObject go)
        {
            var behaviours = go.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null || behaviour is Camera) continue;
                behaviour.enabled = false;
            }
            try
            {
                var parts = go.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (part == null) continue;
                    var typeName = part.GetType().Name;
                    if (typeName != "Rigidbody" && typeName != "Rigidbody2D" && !typeName.EndsWith("Collider") && !typeName.EndsWith("Collider2D"))
                        continue;
                    var enabled = part.GetType().GetProperty("enabled");
                    if (enabled != null && enabled.CanWrite && enabled.PropertyType == typeof(bool))
                        enabled.SetValue(part, false, null);
                    var kinematic = part.GetType().GetProperty("isKinematic");
                    if (kinematic != null && kinematic.CanWrite && kinematic.PropertyType == typeof(bool))
                        kinematic.SetValue(part, true, null);
                }
            }
            catch (Exception)
            {
            }
        }

        static bool Hex(string tint, out Color color)
        {
            color = Color.white;
            if (tint == null || tint.Length != 7 || tint[0] != '#') return false;
            int r, g, b;
            if (!int.TryParse(tint.Substring(1, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out r)) return false;
            if (!int.TryParse(tint.Substring(3, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out g)) return false;
            if (!int.TryParse(tint.Substring(5, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out b)) return false;
            color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return true;
        }
    }
}
