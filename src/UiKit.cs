using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Scripts
{
    /// <summary>Shared look: palette, materials, procedural rounded meshes and TextMeshPro factory.</summary>
    internal static class UiKit
    {
        // Palette: dark slate panels, warm white text, Gorilla Tag leaf green accent.
        public static readonly Color Panel = new Color(0.075f, 0.085f, 0.10f, 1f);
        public static readonly Color PanelEdge = new Color(0.32f, 0.70f, 0.30f, 1f);
        public static readonly Color Button = new Color(0.19f, 0.21f, 0.24f, 1f);
        public static readonly Color ButtonHover = new Color(0.27f, 0.30f, 0.34f, 1f);
        public static readonly Color ButtonActive = new Color(0.30f, 0.62f, 0.27f, 1f);
        public static readonly Color Accent = new Color(0.47f, 0.86f, 0.36f, 1f);
        public static readonly Color Warn = new Color(0.98f, 0.72f, 0.25f, 1f);
        public static readonly Color Stop = new Color(0.93f, 0.36f, 0.33f, 1f);
        public static readonly Color Text = new Color(0.96f, 0.95f, 0.92f, 1f);
        public static readonly Color TextDim = new Color(0.64f, 0.67f, 0.70f, 1f);

        public const int QueueBase = 3100;

        public static TMP_FontAsset Font { get; private set; }

        private static Shader _quadShader;
        private static readonly Dictionary<string, Mesh> _meshCache = new Dictionary<string, Mesh>();

        public static bool Init()
        {
            if (Font != null && _quadShader != null) return true;

            if (_quadShader == null)
                _quadShader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");

            if (Font == null)
                Font = PickFont();

            if (Font == null || _quadShader == null)
            {
                return false;
            }

            Plugin.Log.LogInfo($"UI font: {Font.name}, quad shader: {_quadShader.name}");
            return true;
        }

        private static TMP_FontAsset PickFont()
        {
            TMP_FontAsset[] all;
            try
            {
                all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            }
            catch
            {
                all = Array.Empty<TMP_FontAsset>();
            }

            // Prefer a plain, highly legible sans face; fall back to TMP's default, then anything usable.
            string[] preferred = { "LiberationSans SDF", "Roboto", "Inter", "Arial", "NotoSans", "Montserrat" };
            foreach (string p in preferred)
            {
                TMP_FontAsset hit = all.FirstOrDefault(f => f != null && f.material != null && f.name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0
                                                            && f.name.IndexOf("Fallback", StringComparison.OrdinalIgnoreCase) < 0);
                if (hit != null) return hit;
            }

            try
            {
                TMP_FontAsset def = TMP_Settings.defaultFontAsset;
                if (def != null && def.material != null) return def;
            }
            catch
            {
                // TMP settings asset may be missing in builds.
            }

            return all.FirstOrDefault(f => f != null && f.material != null && f.name.IndexOf("Fallback", StringComparison.OrdinalIgnoreCase) < 0)
                   ?? all.FirstOrDefault(f => f != null && f.material != null);
        }

        /// <summary>Unlit transparent material. alwaysOnTop draws through world geometry (used by the teleprompter).</summary>
        public static Material QuadMaterial(int queueOffset, bool alwaysOnTop)
        {
            var m = new Material(_quadShader) { renderQueue = QueueBase + queueOffset };
            m.mainTexture = Texture2D.whiteTexture;
            SetZTest(m, alwaysOnTop);
            return m;
        }

        public static void SetZTest(Material m, bool alwaysOnTop)
        {
            var mode = alwaysOnTop ? CompareFunction.Always : CompareFunction.LessEqual;
            // UI/Default and the TMP shaders read "ZTest [unity_GUIZTestMode]"; a per-material value overrides the global.
            m.SetFloat("unity_GUIZTestMode", (float)mode);
            if (m.HasProperty("_ZTest")) m.SetFloat("_ZTest", (float)mode);
        }

        public static TextMeshPro MakeText(Transform parent, string name, string text, float fontSize, Color color,
            TextAlignmentOptions align, Vector2 size, int queueOffset, bool alwaysOnTop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = Font;
            var mat = new Material(Font.material) { renderQueue = QueueBase + queueOffset };
            SetZTest(mat, alwaysOnTop);
            tmp.fontSharedMaterial = mat;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.richText = false;
            tmp.text = text;
            // TMP 3D: 1 rect unit = 1 local unit. We work in centimeters so font sizes stay intuitive.
            go.transform.localScale = Vector3.one * 0.01f;
            tmp.rectTransform.sizeDelta = size * 100f;
            return tmp;
        }

        /// <summary>A quad child with its own material instance (so color/alpha can animate per element).</summary>
        public static MeshRenderer MakeShape(Transform parent, string name, Mesh mesh, Color color, int queueOffset, bool alwaysOnTop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = QuadMaterial(queueOffset, alwaysOnTop);
            mr.sharedMaterial.color = color;
            return mr;
        }

        /// <summary>Rounded rectangle in the XY plane centered at origin, with a thin alpha-feathered rim for clean edges.</summary>
        public static Mesh RoundedRect(float w, float h, float radius, float feather = 0.0025f)
        {
            string key = $"rr:{w:F4}:{h:F4}:{radius:F4}:{feather:F4}";
            if (_meshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            radius = Mathf.Clamp(radius, 0f, Mathf.Min(w, h) * 0.5f);
            const int seg = 8;
            var outline = new List<Vector2>();
            Vector2[] centers =
            {
                new Vector2(w * 0.5f - radius, h * 0.5f - radius),
                new Vector2(-w * 0.5f + radius, h * 0.5f - radius),
                new Vector2(-w * 0.5f + radius, -h * 0.5f + radius),
                new Vector2(w * 0.5f - radius, -h * 0.5f + radius),
            };
            for (int c = 0; c < 4; c++)
            {
                for (int i = 0; i <= seg; i++)
                {
                    float a = (c * 90f + i * 90f / seg) * Mathf.Deg2Rad;
                    outline.Add(centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
            }

            int n = outline.Count;
            var verts = new List<Vector3> { Vector3.zero };
            var cols = new List<Color32> { new Color32(255, 255, 255, 255) };
            for (int i = 0; i < n; i++) { verts.Add(outline[i]); cols.Add(new Color32(255, 255, 255, 255)); }
            for (int i = 0; i < n; i++)
            {
                Vector2 p = outline[i];
                Vector2 dir = (p - ClosestCenter(p, centers)).normalized;
                if (dir == Vector2.zero) dir = p.normalized;
                verts.Add(p + dir * feather);
                cols.Add(new Color32(255, 255, 255, 0));
            }

            var tris = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % n;
                tris.Add(0); tris.Add(b); tris.Add(a);
                int oa = 1 + n + i, ob = 1 + n + (i + 1) % n;
                tris.Add(a); tris.Add(b); tris.Add(ob);
                tris.Add(a); tris.Add(ob); tris.Add(oa);
            }

            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            _meshCache[key] = mesh;
            return mesh;
        }

        private static Vector2 ClosestCenter(Vector2 p, Vector2[] centers)
        {
            Vector2 best = centers[0];
            float bd = float.MaxValue;
            foreach (Vector2 c in centers)
            {
                float d = (p - c).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        /// <summary>Simple filled triangle pointing +X (play icon / reading cue).</summary>
        public static Mesh Triangle(float w, float h)
        {
            string key = $"tri:{w:F4}:{h:F4}";
            if (_meshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            var mesh = new Mesh { name = key };
            mesh.SetVertices(new List<Vector3> { new Vector3(-w * 0.5f, h * 0.5f), new Vector3(w * 0.5f, 0f), new Vector3(-w * 0.5f, -h * 0.5f) });
            mesh.SetColors(new List<Color32> { Color.white, Color.white, Color.white });
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.RecalculateBounds();
            _meshCache[key] = mesh;
            return mesh;
        }

        public static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            return t * t * t + 1f;
        }

        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.4f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        /// <summary>Frame-rate independent exponential smoothing factor.</summary>
        public static float Damp(float rate, float dt) => 1f - Mathf.Exp(-rate * dt);
    }

    /// <summary>A set of shapes and texts whose colors are multiplied by a shared alpha (fade in/out).</summary>
    internal sealed class UiGroup
    {
        private sealed class ShapeEntry { public Material Mat; public Color Base; }
        private sealed class TextEntry { public TMP_Text Text; public Color Base; }

        private readonly Dictionary<Renderer, ShapeEntry> _shapes = new Dictionary<Renderer, ShapeEntry>();
        private readonly Dictionary<TMP_Text, TextEntry> _texts = new Dictionary<TMP_Text, TextEntry>();
        private float _alpha = 1f;

        public float Alpha
        {
            get => _alpha;
            set
            {
                if (Mathf.Approximately(value, _alpha)) return;
                _alpha = value;
                foreach (ShapeEntry e in _shapes.Values) Apply(e);
                foreach (TextEntry e in _texts.Values) Apply(e);
            }
        }

        public void Add(MeshRenderer r, Color c)
        {
            var e = new ShapeEntry { Mat = r.sharedMaterial, Base = c };
            _shapes[r] = e;
            Apply(e);
        }

        public void Add(TMP_Text t, Color c)
        {
            var e = new TextEntry { Text = t, Base = c };
            _texts[t] = e;
            Apply(e);
        }

        public void SetColor(MeshRenderer r, Color c)
        {
            if (_shapes.TryGetValue(r, out ShapeEntry e) && e.Base != c) { e.Base = c; Apply(e); }
        }

        public void SetColor(TMP_Text t, Color c)
        {
            if (_texts.TryGetValue(t, out TextEntry e) && e.Base != c) { e.Base = c; Apply(e); }
        }

        private void Apply(ShapeEntry e)
        {
            Color c = e.Base;
            c.a *= _alpha;
            e.Mat.color = c;
        }

        private void Apply(TextEntry e)
        {
            Color c = e.Base;
            c.a *= _alpha;
            e.Text.color = c;
        }
    }
}
