using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace Scripts
{
    public enum PlayState { Ready, Playing, Paused, Ended }

    /// <summary>
    /// The floating script panel. Local only; its renderers are registered with CameraHider so only the headset
    /// camera draws them. Owns playback (scroll offset) so the settings menu can open and close freely.
    /// </summary>
    internal sealed class Teleprompter : MonoBehaviour
    {
        private const float CharsPerChunk = 2500; // TMP meshes use 16-bit indices; keep each chunk well under the limit
        private const float PadSide = 0.045f, PadTop = 0.035f, PadBottom = 0.065f;
        private const float FadeTime = 0.22f;

        public PlayState State { get; private set; } = PlayState.Ready;
        public string FileName { get; private set; } = "";
        public string Error { get; private set; }
        public int LineCount { get; private set; }
        public float Progress => _maxOffset > 0f ? Mathf.Clamp01(_offset / _maxOffset) : (State == PlayState.Ended ? 1f : 0f);
        public bool IsVisible => Plugin.Visible.Value;

        private sealed class Chunk
        {
            public TextMeshPro Tmp;
            public MeshRenderer Renderer;
            public float Top;    // meters below text root
            public float Height; // meters
        }

        private readonly List<Chunk> _chunks = new List<Chunk>();
        private readonly UiGroup _group = new UiGroup();

        private Transform _content, _textRoot, _cueL, _cueR;
        private MeshRenderer _bg, _rim, _track, _fill, _cueLR, _cueRR;
        private TextMeshPro _status, _remaining, _message;
        private string _text = "";

        private float _offset, _maxOffset, _lineHeight = 0.05f, _totalHeight;
        private bool _layoutDirty = true, _colorsDirty = true, _snapNext = true;
        private float _fade = 1f;
        private float _bodyYaw;
        private bool _recentering;
        private float _smoothedHeadY;
        private float _w, _h;

        private void Awake()
        {
            _content = new GameObject("Content").transform;
            _content.SetParent(transform, false);

            Plugin.TextSize.SettingChanged += (_, __) => _layoutDirty = true;
            Plugin.PanelWidth.SettingChanged += (_, __) => _layoutDirty = true;
            Plugin.PanelHeight.SettingChanged += (_, __) => _layoutDirty = true;
            Plugin.ReadingLine.SettingChanged += (_, __) => _layoutDirty = true;
            Plugin.Follow.SettingChanged += (_, __) => { _recentering = true; };

            _fade = Plugin.Visible.Value ? 1f : 0f;
            _content.gameObject.SetActive(_fade > 0f);
        }

        // ---------------------------------------------------------------- playback API

        public void Play()
        {
            if (State == PlayState.Ended) _offset = 0f;
            State = PlayState.Playing;
            if (!IsVisible) SetVisible(true);
        }

        public void Pause()
        {
            if (State == PlayState.Playing) State = PlayState.Paused;
        }

        public void TogglePlay()
        {
            if (State == PlayState.Playing) Pause();
            else Play();
        }

        public void Restart()
        {
            _offset = 0f;
            _colorsDirty = true;
            State = PlayState.Ready;
        }

        /// <summary>Recording sync: begin a take from the top.</summary>
        public void StartTake()
        {
            _offset = 0f;
            State = PlayState.Playing;
        }

        public void SetVisible(bool v)
        {
            Plugin.Visible.Value = v;
            if (v) _snapNext = _fade <= 0f;
        }

        public void ToggleVisible() => SetVisible(!Plugin.Visible.Value);

        public void LoadScript(LoadedScript s, bool keepPosition)
        {
            float keep = _offset;
            FileName = s.FileName;
            Error = s.Error;
            _text = s.Text ?? "";
            LineCount = _text.Length == 0 ? 0 : _text.Split('\n').Length;
            _layoutDirty = true;
            if (keepPosition)
            {
                _offset = keep; // clamped after layout
            }
            else
            {
                _offset = 0f;
                State = PlayState.Ready;
            }
        }

        public float SecondsRemaining
        {
            get
            {
                float speed = Mathf.Max(0.01f, Plugin.Speed.Value) * _lineHeight;
                return Mathf.Max(0f, _maxOffset - _offset) / speed;
            }
        }

        // ---------------------------------------------------------------- frame

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            // Show/hide fade (playback keeps running while hidden).
            float targetFade = IsVisible ? 1f : 0f;
            if (!Mathf.Approximately(_fade, targetFade))
            {
                _fade = Mathf.MoveTowards(_fade, targetFade, dt / FadeTime);
                _colorsDirty = true;
            }
            bool shown = _fade > 0.001f;
            if (_content.gameObject.activeSelf != shown)
            {
                _content.gameObject.SetActive(shown);
                _colorsDirty = true;
            }

            if (_layoutDirty) RebuildLayout();

            if (State == PlayState.Playing)
            {
                _offset += Plugin.Speed.Value * _lineHeight * Time.deltaTime;
                if (_offset >= _maxOffset)
                {
                    _offset = _maxOffset;
                    State = PlayState.Ended; // stop at the end; no auto loop
                }
                _colorsDirty = true;
            }

            if (!shown) return;

            Follow(dt);
            UpdateVisuals();
        }

        // ---------------------------------------------------------------- following

        private void Follow(float dt)
        {
            Transform head = Rig.Head;
            if (head == null) return;

            float scale = Rig.Scale;
            float dist = Plugin.Distance.Value * scale;
            float vOff = Plugin.VerticalOffset.Value * scale;
            Vector3 headPos = head.position;
            Vector3 fwd = head.forward;
            float headYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;

            Vector3 target;
            if (Plugin.Follow.Value == FollowMode.Head)
            {
                // Follow the view, but limit pitch so looking straight up/down doesn't fling the panel overhead.
                float pitch = -Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
                pitch = Mathf.Clamp(pitch, -45f, 45f);
                Quaternion look = Quaternion.Euler(pitch, headYaw, 0f);
                target = headPos + look * new Vector3(0f, 0f, dist) + Vector3.up * vOff;
                _bodyYaw = headYaw;
                _smoothedHeadY = headPos.y;
            }
            else
            {
                // Body: anchored to the body, re-centers only when you turn well away from it.
                float delta = Mathf.DeltaAngle(_bodyYaw, headYaw);
                if (Mathf.Abs(delta) > 40f) _recentering = true;
                if (_recentering)
                {
                    _bodyYaw = Mathf.LerpAngle(_bodyYaw, headYaw, UiKit.Damp(3f, dt));
                    if (Mathf.Abs(Mathf.DeltaAngle(_bodyYaw, headYaw)) < 2f) _recentering = false;
                }

                _smoothedHeadY = Mathf.Lerp(_smoothedHeadY, headPos.y, UiKit.Damp(1.5f, dt));
                Transform body = Rig.Body;
                Vector3 basePos = body != null ? body.position : headPos;
                basePos.y = _smoothedHeadY;
                target = basePos + Quaternion.Euler(0f, _bodyYaw, 0f) * new Vector3(0f, 0f, dist) + Vector3.up * vOff;
            }

            Vector3 toPanel = target - headPos;
            Quaternion targetRot = toPanel.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toPanel, Vector3.up) : transform.rotation;

            if (_snapNext || (transform.position - target).sqrMagnitude > 25f * scale * scale)
            {
                transform.SetPositionAndRotation(target, targetRot);
                _snapNext = false;
                _bodyYaw = headYaw;
                _smoothedHeadY = headPos.y;
            }
            else
            {
                float k = UiKit.Damp(Plugin.Smoothing.Value, dt);
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, target, k), Quaternion.Slerp(transform.rotation, targetRot, k));
            }
            transform.localScale = Vector3.one * scale;
        }

        // ---------------------------------------------------------------- layout

        private void RebuildLayout()
        {
            _layoutDirty = false;
            float prevMax = _maxOffset;
            float prevFrac = prevMax > 0f ? _offset / prevMax : 0f;

            _w = Plugin.PanelWidth.Value;
            _h = Plugin.PanelHeight.Value;

            if (_bg == null) BuildChrome();
            LayoutChrome();

            foreach (Chunk c in _chunks) Destroy(c.Tmp.gameObject);
            _chunks.Clear();

            float textW = _w - PadSide * 2f;
            string body = string.IsNullOrEmpty(Error) ? _text : "";
            _message.text = string.IsNullOrEmpty(Error) ? "" : Error + "\n\nAdd a .txt file to BepInEx/config/Scripts and press Reload.";

            float y = 0f;
            float lineH = 0f;
            foreach (string piece in SplitChunks(body))
            {
                TextMeshPro t = UiKit.MakeText(_textRoot, "Chunk", piece, Plugin.TextSize.Value, UiKit.Text,
                    TextAlignmentOptions.TopLeft, new Vector2(textW, 10f), 2, true);
                t.rectTransform.pivot = new Vector2(0f, 1f);
                t.lineSpacing = 8f;
                t.ForceMeshUpdate();
                float hgt = t.preferredHeight * 0.01f;
                if (lineH <= 0f)
                {
                    // Line pitch = height of two lines minus one; works even while the panel is hidden.
                    float one = t.GetPreferredValues("Ag", textW * 100f, 0f).y;
                    float two = t.GetPreferredValues("Ag" + "\n" + "Ag", textW * 100f, 0f).y;
                    lineH = Mathf.Max(0.001f, (two - one) * 0.01f);
                }
                t.rectTransform.sizeDelta = new Vector2(textW * 100f, Mathf.Max(hgt, 0.01f) * 100f);
                t.transform.localPosition = new Vector3(0f, -y, 0f);
                t.ForceMeshUpdate();
                var mr = t.GetComponent<MeshRenderer>();
                var chunk = new Chunk { Tmp = t, Renderer = mr, Top = y, Height = hgt };
                // TMP can regenerate its mesh on its own (enable, font atlas updates); re-apply the fade right before upload.
                t.OnPreRenderText += info => ApplyFade(chunk, info);
                _chunks.Add(chunk);
                y += hgt;
            }
            CameraHider.Register(gameObject);

            _lineHeight = lineH > 0f ? lineH : Plugin.TextSize.Value * 0.0012f;
            _totalHeight = y;
            _maxOffset = Mathf.Max(0f, _totalHeight - _lineHeight);
            _offset = prevMax > 0f ? Mathf.Clamp(prevFrac * _maxOffset, 0f, _maxOffset) : Mathf.Clamp(_offset, 0f, _maxOffset);
            if (State == PlayState.Ended && _offset < _maxOffset) State = PlayState.Paused;
            _colorsDirty = true;
        }

        private static IEnumerable<string> SplitChunks(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            string[] lines = text.Split('\n');
            var sb = new StringBuilder();
            foreach (string line in lines)
            {
                if (sb.Length > 0 && sb.Length + line.Length > CharsPerChunk)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                if (sb.Length > 0) sb.Append('\n');
                // An empty line still needs height; a zero-width space keeps TMP from collapsing it at chunk edges.
                sb.Append(line.Length == 0 ? "​" : line);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        private void BuildChrome()
        {
            _rim = UiKit.MakeShape(_content, "Rim", UiKit.RoundedRect(1, 1, 0.1f), UiKit.PanelEdge, -1, true);
            _bg = UiKit.MakeShape(_content, "Background", UiKit.RoundedRect(1, 1, 0.1f), UiKit.Panel, 0, true);

            _textRoot = new GameObject("TextRoot").transform;
            _textRoot.SetParent(_content, false);

            _cueL = UiKit.MakeShape(_content, "CueL", UiKit.Triangle(0.018f, 0.024f), UiKit.Accent, 3, true).transform;
            _cueR = UiKit.MakeShape(_content, "CueR", UiKit.Triangle(0.018f, 0.024f), UiKit.Accent, 3, true).transform;
            _cueR.localRotation = Quaternion.Euler(0f, 0f, 180f);
            _cueLR = _cueL.GetComponent<MeshRenderer>();
            _cueRR = _cueR.GetComponent<MeshRenderer>();

            _track = UiKit.MakeShape(_content, "Track", UiKit.RoundedRect(1f, 1f, 0.5f, 0.001f), new Color(1f, 1f, 1f, 0.12f), 1, true);
            _fill = UiKit.MakeShape(_content, "Fill", UiKit.RoundedRect(1f, 1f, 0.5f, 0.001f), UiKit.Accent, 2, true);

            _status = UiKit.MakeText(_content, "Status", "", 16f, UiKit.TextDim, TextAlignmentOptions.MidlineLeft, new Vector2(0.6f, 0.04f), 3, true);
            _status.textWrappingMode = TextWrappingModes.NoWrap;
            _remaining = UiKit.MakeText(_content, "Remaining", "", 16f, UiKit.TextDim, TextAlignmentOptions.MidlineRight, new Vector2(0.4f, 0.04f), 3, true);
            _remaining.textWrappingMode = TextWrappingModes.NoWrap;
            _message = UiKit.MakeText(_content, "Message", "", 26f, UiKit.Warn, TextAlignmentOptions.Center, new Vector2(0.8f, 0.3f), 3, true);

            _group.Add(_rim, UiKit.PanelEdge);
            _group.Add(_bg, UiKit.Panel);
            _group.Add(_cueLR, UiKit.Accent);
            _group.Add(_cueRR, UiKit.Accent);
            _group.Add(_track, new Color(1f, 1f, 1f, 0.12f));
            _group.Add(_fill, UiKit.Accent);
            _group.Add(_status, UiKit.TextDim);
            _group.Add(_remaining, UiKit.TextDim);
            _group.Add(_message, UiKit.Warn);
        }

        private void LayoutChrome()
        {
            float r = 0.035f;
            _bg.GetComponent<MeshFilter>().sharedMesh = UiKit.RoundedRect(_w, _h, r);
            _rim.GetComponent<MeshFilter>().sharedMesh = UiKit.RoundedRect(_w + 0.006f, _h + 0.006f, r + 0.003f, 0.003f);
            _rim.transform.localPosition = new Vector3(0f, 0f, 0.001f);

            float textTop = _h * 0.5f - PadTop;
            float textBottom = -_h * 0.5f + PadBottom;
            float readY = Mathf.Lerp(textTop, textBottom, Plugin.ReadingLine.Value);
            _cueL.localPosition = new Vector3(-_w * 0.5f + 0.02f, readY, -0.003f);
            _cueR.localPosition = new Vector3(_w * 0.5f - 0.02f, readY, -0.003f);

            float barY = -_h * 0.5f + 0.022f;
            float barW = _w - PadSide * 2f;
            _track.GetComponent<MeshFilter>().sharedMesh = UiKit.RoundedRect(barW, 0.006f, 0.003f, 0.001f);
            _track.transform.localPosition = new Vector3(0f, barY, -0.002f);
            _fill.transform.localPosition = new Vector3(-barW * 0.5f, barY, -0.003f);

            float statusY = barY + 0.022f;
            _status.rectTransform.sizeDelta = new Vector2(barW * 0.65f, 4f) * 100f;
            _status.transform.localPosition = new Vector3(-barW * 0.5f + barW * 0.325f, statusY, -0.003f);
            _remaining.rectTransform.sizeDelta = new Vector2(barW * 0.35f, 4f) * 100f;
            _remaining.transform.localPosition = new Vector3(barW * 0.5f - barW * 0.175f, statusY, -0.003f);
            _message.rectTransform.sizeDelta = new Vector2(_w - 0.1f, _h - 0.1f) * 100f;
            _message.transform.localPosition = new Vector3(0f, 0.01f, -0.003f);
        }

        // ---------------------------------------------------------------- per-frame visuals

        private float _lastFill = -1f;
        private string _lastStatus, _lastRemaining;

        private void UpdateVisuals()
        {
            Color bg = UiKit.Panel;
            bg.a = Plugin.Opacity.Value;
            _group.SetColor(_bg, bg);
            Color rim = UiKit.PanelEdge;
            rim.a = Plugin.Opacity.Value * 0.55f;
            _group.SetColor(_rim, rim);
            _group.Alpha = UiKit.EaseOutCubic(_fade);

            // Progress bar
            float barW = _w - PadSide * 2f;
            float fillW = Mathf.Max(0.006f, barW * Progress);
            if (!Mathf.Approximately(fillW, _lastFill))
            {
                _lastFill = fillW;
                _fill.GetComponent<MeshFilter>().sharedMesh = UiKit.RoundedRect(Mathf.Round(fillW * 500f) / 500f, 0.006f, 0.003f, 0.001f);
                _fill.transform.localPosition = new Vector3(-barW * 0.5f + Mathf.Round(fillW * 500f) / 1000f, _fill.transform.localPosition.y, -0.003f);
            }

            Color stateColor;
            string stateText;
            switch (State)
            {
                case PlayState.Playing: stateText = "PLAYING"; stateColor = UiKit.Accent; break;
                case PlayState.Paused: stateText = "PAUSED"; stateColor = UiKit.Warn; break;
                case PlayState.Ended: stateText = "END"; stateColor = UiKit.Stop; break;
                default: stateText = "READY"; stateColor = UiKit.TextDim; break;
            }
            _group.SetColor(_cueLR, State == PlayState.Playing ? UiKit.Accent : stateColor);
            _group.SetColor(_cueRR, State == PlayState.Playing ? UiKit.Accent : stateColor);

            string status = $"{stateText}   {Mathf.RoundToInt(Progress * 100f)}%   {FileName}";
            if (status != _lastStatus) { _lastStatus = status; _status.text = status; }
            float secs = SecondsRemaining;
            string rem = State == PlayState.Ended ? "Press Restart" : $"{(int)(secs / 60f)}:{(int)(secs % 60f):00} left";
            if (rem != _lastRemaining) { _lastRemaining = rem; _remaining.text = rem; }

            // Scroll position + per-character fade at the top and bottom of the text window.
            float textTop = _h * 0.5f - PadTop;
            float textBottom = -_h * 0.5f + PadBottom + 0.02f;
            float readY = Mathf.Lerp(textTop, _h * -0.5f + PadBottom, Plugin.ReadingLine.Value);
            float rootY = readY + _lineHeight * 0.5f + _offset;
            Vector3 rootPos = new Vector3(-(_w * 0.5f - PadSide), rootY, -0.002f);
            if (_textRoot.localPosition != rootPos) { _textRoot.localPosition = rootPos; _colorsDirty = true; }

            if (!_colorsDirty) return;
            _colorsDirty = false;

            _fadeTop = textTop;
            _fadeBottom = textBottom;
            _fadeBand = Mathf.Max(0.02f, (textTop - textBottom) * 0.16f);
            _rootY = rootY;

            foreach (Chunk c in _chunks)
            {
                float cTop = rootY - c.Top;
                float cBottom = cTop - c.Height;
                bool any = cBottom < textTop && cTop > textBottom;
                if (c.Renderer.enabled != any) c.Renderer.enabled = any;
                if (!any) continue;
                ApplyFade(c, c.Tmp.textInfo);
                c.Tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            }
        }

        private float _fadeTop, _fadeBottom, _fadeBand = 0.05f, _rootY;

        /// <summary>Per-character alpha: fully visible inside the window, smooth fade near the top and bottom edges.</summary>
        private void ApplyFade(Chunk c, TMP_TextInfo info)
        {
            if (info == null || info.characterInfo == null) return;
            float groupA = _group.Alpha;
            Color32 baseCol = UiKit.Text;
            for (int i = 0; i < info.characterCount; i++)
            {
                TMP_CharacterInfo ch = info.characterInfo[i];
                if (!ch.isVisible) continue;
                float midY = _rootY - c.Top + (ch.topLeft.y + ch.bottomLeft.y) * 0.5f * 0.01f;
                float a = Mathf.Clamp01((_fadeTop - midY) / _fadeBand) * Mathf.Clamp01((midY - _fadeBottom) / _fadeBand);
                a = a * a * (3f - 2f * a);
                var col = new Color32(baseCol.r, baseCol.g, baseCol.b, (byte)(a * groupA * 255f));
                if (ch.materialReferenceIndex >= info.meshInfo.Length) continue;
                Color32[] cols = info.meshInfo[ch.materialReferenceIndex].colors32;
                int v = ch.vertexIndex;
                if (cols == null || v + 3 >= cols.Length) continue;
                cols[v] = col; cols[v + 1] = col; cols[v + 2] = col; cols[v + 3] = col;
            }
        }
    }
}

