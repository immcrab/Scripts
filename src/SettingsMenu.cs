using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace Scripts
{
    /// <summary>
    /// Finger-operated settings panel, separate from the teleprompter: opening or closing it never touches
    /// playback. It is placed in front of you when opened and stays put in the world so it is easy to press.
    /// Two tabs: CONTROLS (playback + settings) and SCRIPTS (pick a script, open the scripts folder).
    /// </summary>
    internal sealed class SettingsMenu : MonoBehaviour
    {
        private const float W = 0.50f, H = 0.645f, CenterY = -0.03f;
        private const float OpenTime = 0.2f, CloseTime = 0.14f;
        private const int ListRows = 5;

        private enum Page { Controls, Scripts }

        public bool IsOpen { get; private set; }

        private readonly UiGroup _group = new UiGroup();
        private readonly List<VrButton> _buttons = new List<VrButton>();
        private Transform _content, _controlsPage, _scriptsPage;
        private Page _page = Page.Controls;

        private MeshRenderer _pill;
        private TextMeshPro _pillText, _scriptLine, _syncInfo, _camInfo;
        private VrButton _playBtn, _showBtn, _syncBtn, _camBtn;

        // Scripts page
        private readonly VrButton[] _rows = new VrButton[ListRows];
        private TextMeshPro _pageText, _emptyText, _folderFeedback;
        private VrButton _prevPage, _nextPage;
        private int _listPage;
        private float _feedbackUntil;

        private float _t;              // 0 closed .. 1 open
        private float _openedAt;
        private float _cooldownUntil;
        private Vector3 _targetPos;
        private Quaternion _targetRot;

        private void Awake()
        {
            _content = new GameObject("Content").transform;
            _content.SetParent(transform, false);
            Build();
            CameraHider.Register(gameObject);
            ShowPage(Page.Controls);
            _content.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            IsOpen = true;
            _openedAt = Time.unscaledTime;
            if (_t <= 0.01f) PlaceInFront(snap: true);
            _content.gameObject.SetActive(true);
            Plugin.Library.Refresh(); // pick up scripts added while the game was running
            foreach (VrButton b in _buttons) b.ResetInput();
        }

        public void Close()
        {
            IsOpen = false;
        }

        // ---------------------------------------------------------------- build

        private void Build()
        {
            var rim = UiKit.MakeShape(_content, "Rim", UiKit.RoundedRect(W + 0.008f, H + 0.008f, 0.034f, 0.003f), UiKit.PanelEdge, -1, false);
            rim.transform.localPosition = new Vector3(0f, CenterY, 0.001f);
            _group.Add(rim, new Color(UiKit.PanelEdge.r, UiKit.PanelEdge.g, UiKit.PanelEdge.b, 0.9f));
            var bg = UiKit.MakeShape(_content, "Background", UiKit.RoundedRect(W, H, 0.03f), UiKit.Panel, 0, false);
            bg.transform.localPosition = new Vector3(0f, CenterY, 0f);
            _group.Add(bg, new Color(UiKit.Panel.r, UiKit.Panel.g, UiKit.Panel.b, 0.94f));

            float left = -W * 0.5f + 0.03f;

            // Header (shared)
            Text(_content, "Title", "SCRIPTS", 30f, UiKit.Accent, TextAlignmentOptions.MidlineLeft, new Vector2(0.25f, 0.05f), new Vector3(left + 0.125f, 0.25f, 0f)).fontStyle = FontStyles.Bold;
            _pill = UiKit.MakeShape(_content, "Pill", UiKit.RoundedRect(0.15f, 0.036f, 0.018f), UiKit.Accent, 1, false);
            _pill.transform.localPosition = new Vector3(W * 0.5f - 0.03f - 0.075f, 0.25f, -0.001f);
            _group.Add(_pill, UiKit.Accent);
            _pillText = Text(_content, "PillText", "READY", 14f, UiKit.Panel, TextAlignmentOptions.Center, new Vector2(0.15f, 0.036f), _pill.transform.localPosition + new Vector3(0f, 0f, -0.002f));
            _pillText.fontStyle = FontStyles.Bold;

            // Tabs (shared)
            var tabControls = Btn(_content, "TabControls", "CONTROLS", new Vector2(0.215f, 0.046f), new Vector3(-0.1125f, 0.198f, -0.003f), () => ShowPage(Page.Controls), 15f);
            tabControls.IsActive = () => _page == Page.Controls;
            var tabScripts = Btn(_content, "TabScripts", "CHOOSE SCRIPT", new Vector2(0.215f, 0.046f), new Vector3(0.1125f, 0.198f, -0.003f), () => ShowPage(Page.Scripts), 15f);
            tabScripts.IsActive = () => _page == Page.Scripts;

            _controlsPage = new GameObject("ControlsPage").transform;
            _controlsPage.SetParent(_content, false);
            _scriptsPage = new GameObject("ScriptsPage").transform;
            _scriptsPage.SetParent(_content, false);

            BuildControls(_controlsPage, left);
            BuildScripts(_scriptsPage, left);
        }

        private void BuildControls(Transform page, float left)
        {
            // Transport
            float ty = 0.14f;
            _playBtn = Btn(page, "Play", "PLAY", new Vector2(0.125f, 0.06f), new Vector3(-0.165f, ty, -0.003f), () => Plugin.Prompter.TogglePlay(), 19f);
            _playBtn.IsActive = () => Plugin.Prompter.State == PlayState.Playing;
            Btn(page, "Restart", "RESTART", new Vector2(0.1f, 0.06f), new Vector3(-0.045f, ty, -0.003f), () => Plugin.Prompter.Restart(), 15f);
            _showBtn = Btn(page, "ShowHide", "SHOW", new Vector2(0.1f, 0.06f), new Vector3(0.065f, ty, -0.003f), () => Plugin.Prompter.ToggleVisible(), 15f);
            Btn(page, "Reload", "RELOAD", new Vector2(0.1f, 0.06f), new Vector3(0.175f, ty, -0.003f), Reload, 15f);

            Divider(page, 0.097f);

            // Sliders
            Stepper(page, "Speed", Plugin.Speed, 0.05f, 0.055f, v => $"{v:0.00} lines/s");
            Stepper(page, "Text Size", Plugin.TextSize, 4f, 0.0f, v => $"{v:0}");
            Stepper(page, "Distance", Plugin.Distance, 0.1f, -0.055f, v => $"{v:0.0} m");
            Stepper(page, "Opacity", Plugin.Opacity, 0.05f, -0.11f, v => $"{Mathf.RoundToInt(v * 100f)}%");

            // Follow mode
            float fy = -0.17f;
            Text(page, "FollowLabel", "Follow", 18f, UiKit.Text, TextAlignmentOptions.MidlineLeft, new Vector2(0.2f, 0.04f), new Vector3(left + 0.1f, fy, 0f));
            var head = Btn(page, "Head", "HEAD", new Vector2(0.095f, 0.044f), new Vector3(0.1f, fy, -0.003f), () => Plugin.Follow.Value = FollowMode.Head, 15f);
            head.IsActive = () => Plugin.Follow.Value == FollowMode.Head;
            var body = Btn(page, "Body", "BODY", new Vector2(0.095f, 0.044f), new Vector3(0.2f, fy, -0.003f), () => Plugin.Follow.Value = FollowMode.Body, 15f);
            body.IsActive = () => Plugin.Follow.Value == FollowMode.Body;

            // Recording sync
            float sy = -0.225f;
            Text(page, "SyncLabel", "Rec sync", 18f, UiKit.Text, TextAlignmentOptions.MidlineLeft, new Vector2(0.2f, 0.04f), new Vector3(left + 0.1f, sy + 0.006f, 0f));
            _syncInfo = Text(page, "SyncInfo", "", 12f, UiKit.TextDim, TextAlignmentOptions.MidlineLeft, new Vector2(0.3f, 0.03f), new Vector3(left + 0.15f, sy - 0.017f, 0f));
            _syncBtn = Btn(page, "Sync", "OFF", new Vector2(0.195f, 0.044f), new Vector3(0.15f, sy, -0.003f), () => Plugin.SyncWithRecording.Value = !Plugin.SyncWithRecording.Value, 15f);
            _syncBtn.IsActive = () => Plugin.SyncWithRecording.Value;

            // Camera visibility (hidden from recording/spectator cameras by default)
            float cy = -0.28f;
            Text(page, "CamLabel", "On camera", 18f, UiKit.Text, TextAlignmentOptions.MidlineLeft, new Vector2(0.2f, 0.04f), new Vector3(left + 0.1f, cy + 0.006f, 0f));
            _camInfo = Text(page, "CamInfo", "", 12f, UiKit.TextDim, TextAlignmentOptions.MidlineLeft, new Vector2(0.3f, 0.03f), new Vector3(left + 0.15f, cy - 0.017f, 0f));
            _camBtn = Btn(page, "ShowOnCam", "HIDDEN", new Vector2(0.195f, 0.044f), new Vector3(0.15f, cy, -0.003f), () => Plugin.ShowOnCameras.Value = !Plugin.ShowOnCameras.Value, 15f);
            _camBtn.IsActive = () => Plugin.ShowOnCameras.Value;
            _camBtn.ActiveColor = new Color(0.72f, 0.46f, 0.16f, 1f); // warm: a reminder that viewers can see it

            _scriptLine = Text(page, "ScriptLine", "", 12f, UiKit.TextDim, TextAlignmentOptions.Center, new Vector2(W - 0.04f, 0.03f), new Vector3(0f, -0.326f, 0f));
            _scriptLine.overflowMode = TextOverflowModes.Ellipsis;
        }

        private void BuildScripts(Transform page, float left)
        {
            Text(page, "WhereLabel", "Put your scripts (.txt files) in this folder:", 13f, UiKit.TextDim, TextAlignmentOptions.MidlineLeft, new Vector2(W - 0.06f, 0.03f), new Vector3(0f, 0.152f, 0f));
            Text(page, "WherePath", "Gorilla Tag/BepInEx/config/Scripts", 15f, UiKit.Accent, TextAlignmentOptions.MidlineLeft, new Vector2(W - 0.06f, 0.03f), new Vector3(0f, 0.127f, 0f)).fontStyle = FontStyles.Bold;

            Btn(page, "OpenFolder", "OPEN SCRIPTS FOLDER", new Vector2(0.27f, 0.05f), new Vector3(-0.085f, 0.08f, -0.003f), OpenFolder, 14f);
            Btn(page, "RefreshList", "REFRESH", new Vector2(0.16f, 0.05f), new Vector3(0.14f, 0.08f, -0.003f), () => { Plugin.Library.Refresh(); ShowFeedback("List refreshed"); }, 14f);

            _folderFeedback = Text(page, "Feedback", "", 12f, UiKit.Warn, TextAlignmentOptions.Center, new Vector2(W - 0.06f, 0.025f), new Vector3(0f, 0.043f, 0f));

            float y0 = 0.005f, step = 0.058f;
            for (int i = 0; i < ListRows; i++)
            {
                int row = i;
                VrButton b = Btn(page, "Script" + i, "", new Vector2(W - 0.06f, 0.048f), new Vector3(0f, y0 - step * i, -0.003f), () => PickRow(row), 16f);
                b.Label.overflowMode = TextOverflowModes.Ellipsis;
                b.Label.rectTransform.sizeDelta = new Vector2(W - 0.09f, 0.048f) * 100f;
                b.Label.fontStyle = FontStyles.Normal;
                b.IsActive = () => IsRowActive(row);
                _rows[i] = b;
            }

            _emptyText = Text(page, "Empty", "No scripts yet.\nPress OPEN SCRIPTS FOLDER, add a .txt file,\nthen press REFRESH.", 15f, UiKit.Warn, TextAlignmentOptions.Center, new Vector2(W - 0.06f, 0.12f), new Vector3(0f, -0.09f, 0f));
            _emptyText.textWrappingMode = TextWrappingModes.Normal;

            float py = -0.3f;
            _prevPage = Btn(page, "PrevPage", "<", new Vector2(0.06f, 0.044f), new Vector3(-0.12f, py, -0.003f), () => _listPage--, 22f);
            _pageText = Text(page, "PageText", "", 15f, UiKit.TextDim, TextAlignmentOptions.Center, new Vector2(0.16f, 0.04f), new Vector3(0f, py, 0f));
            _nextPage = Btn(page, "NextPage", ">", new Vector2(0.06f, 0.044f), new Vector3(0.12f, py, -0.003f), () => _listPage++, 22f);
        }

        private void Divider(Transform parent, float y)
        {
            var div = UiKit.MakeShape(parent, "Divider", UiKit.RoundedRect(W - 0.06f, 0.002f, 0.001f, 0.0005f), new Color(1f, 1f, 1f, 0.1f), 1, false);
            div.transform.localPosition = new Vector3(0f, y, -0.001f);
            _group.Add(div, new Color(1f, 1f, 1f, 0.1f));
        }

        private TextMeshPro Text(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align, Vector2 box, Vector3 pos)
        {
            TextMeshPro t = UiKit.MakeText(parent, name, text, size, color, align, box, 3, false);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.transform.localPosition = pos + new Vector3(0f, 0f, -0.002f);
            _group.Add(t, color);
            return t;
        }

        private VrButton Btn(Transform parent, string name, string label, Vector2 size, Vector3 pos, System.Action onPress, float font = 20f)
        {
            var b = new VrButton(parent, _group, name, label, size, pos, font) { OnPress = onPress };
            _buttons.Add(b);
            return b;
        }

        private void Stepper(Transform page, string label, ConfigEntry<float> entry, float step, float y, System.Func<float, string> fmt)
        {
            float left = -W * 0.5f + 0.03f;
            Text(page, label + "Label", label, 18f, UiKit.Text, TextAlignmentOptions.MidlineLeft, new Vector2(0.2f, 0.04f), new Vector3(left + 0.1f, y, 0f));
            var range = entry.Description.AcceptableValues as AcceptableValueRange<float>;
            float min = range != null ? range.MinValue : float.MinValue;
            float max = range != null ? range.MaxValue : float.MaxValue;

            Btn(page, label + "Minus", "-", new Vector2(0.05f, 0.044f), new Vector3(0.055f, y, -0.003f), () => entry.Value = Mathf.Clamp(Round(entry.Value - step, step), min, max), 24f);
            TextMeshPro value = Text(page, label + "Value", fmt(entry.Value), 17f, UiKit.Text, TextAlignmentOptions.Center, new Vector2(0.1f, 0.04f), new Vector3(0.135f, y, 0f));
            Btn(page, label + "Plus", "+", new Vector2(0.05f, 0.044f), new Vector3(0.215f, y, -0.003f), () => entry.Value = Mathf.Clamp(Round(entry.Value + step, step), min, max), 24f);

            entry.SettingChanged += (_, __) => value.text = fmt(entry.Value);
        }

        private static float Round(float v, float step) => Mathf.Round(v / step) * step;

        // ---------------------------------------------------------------- actions

        private void ShowPage(Page p)
        {
            _page = p;
            _controlsPage.gameObject.SetActive(p == Page.Controls);
            _scriptsPage.gameObject.SetActive(p == Page.Scripts);
            if (p == Page.Scripts)
            {
                Plugin.Library.Refresh();
                // Jump to the page that holds the current script.
                int idx = IndexOf(Plugin.Prompter != null ? Plugin.Prompter.FileName : null);
                _listPage = idx >= 0 ? idx / ListRows : 0;
            }
            foreach (VrButton b in _buttons) b.ResetInput();
        }

        private static int IndexOf(string file)
        {
            IReadOnlyList<string> files = Plugin.Library.Files;
            for (int i = 0; i < files.Count; i++)
                if (string.Equals(files[i], file, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private bool IsRowActive(int row)
        {
            int i = _listPage * ListRows + row;
            IReadOnlyList<string> files = Plugin.Library.Files;
            return Plugin.Prompter != null && i < files.Count
                   && string.Equals(files[i], Plugin.Prompter.FileName, System.StringComparison.OrdinalIgnoreCase);
        }

        private void PickRow(int row)
        {
            int i = _listPage * ListRows + row;
            IReadOnlyList<string> files = Plugin.Library.Files;
            if (i >= files.Count) return;
            string name = files[i];
            Plugin.ActiveScript.Value = name;
            Plugin.Prompter.LoadScript(Plugin.Library.Load(name), keepPosition: false);
            ShowFeedback("Loaded " + Path.GetFileNameWithoutExtension(name));
            Plugin.Log.LogInfo($"Selected script '{name}' ({Plugin.Prompter.LineCount} lines).");
        }

        private void OpenFolder()
        {
            Plugin.Library.EnsureFolder();
            string folder = Plugin.Library.Folder;
            bool ok = false;
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
                ok = true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("explorer.exe failed, trying OpenURL: " + e.Message);
            }
            if (!ok)
            {
                try
                {
                    Application.OpenURL("file:///" + folder.Replace('\\', '/'));
                    ok = true;
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError("Could not open the scripts folder: " + e.Message);
                }
            }
            ShowFeedback(ok ? "Opened on your PC desktop. Take off your headset to edit." : "Could not open folder. See the BepInEx log for the path.");
            Plugin.Log.LogInfo("Scripts folder: " + folder);
        }

        private void ShowFeedback(string text)
        {
            _folderFeedback.text = text;
            _feedbackUntil = Time.unscaledTime + 4f;
        }

        private void Reload()
        {
            Plugin.Library.Refresh();
            LoadedScript s = Plugin.Library.Load(Plugin.ActiveScript.Value);
            Plugin.Prompter.LoadScript(s, keepPosition: s.FileName == Plugin.Prompter.FileName);
            Plugin.Log.LogInfo($"Reloaded script '{s.FileName}' ({Plugin.Prompter.LineCount} lines).");
        }

        // ---------------------------------------------------------------- frame

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            _t = IsOpen ? Mathf.MoveTowards(_t, 1f, dt / OpenTime) : Mathf.MoveTowards(_t, 0f, dt / CloseTime);

            if (_t <= 0f && !IsOpen)
            {
                if (_content.gameObject.activeSelf) _content.gameObject.SetActive(false);
                return;
            }

            FollowLazily(dt);

            float eased = IsOpen ? UiKit.EaseOutBack(_t) : UiKit.EaseOutCubic(_t);
            float s = Mathf.LerpUnclamped(0.88f, 1f, eased) * Rig.Scale;
            transform.localScale = Vector3.one * s;
            _group.Alpha = UiKit.EaseOutCubic(_t);

            RefreshLabels();
            if (_page == Page.Scripts) RefreshList();

            bool inputEnabled = IsOpen && _t > 0.9f && Time.unscaledTime - _openedAt > 0.25f;
            foreach (VrButton b in _buttons)
                b.Tick(transform, dt, inputEnabled, ref _cooldownUntil);
        }

        private void RefreshList()
        {
            IReadOnlyList<string> files = Plugin.Library.Files;
            int pages = Mathf.Max(1, (files.Count + ListRows - 1) / ListRows);
            _listPage = Mathf.Clamp(_listPage, 0, pages - 1);

            for (int r = 0; r < ListRows; r++)
            {
                int i = _listPage * ListRows + r;
                bool has = i < files.Count;
                if (_rows[r].Root.gameObject.activeSelf != has) _rows[r].Root.gameObject.SetActive(has);
                if (has)
                {
                    string label = Path.GetFileNameWithoutExtension(files[i]);
                    if (IsRowActive(r)) label = "> " + label;
                    _rows[r].SetLabel(label);
                }
            }

            bool empty = files.Count == 0;
            if (_emptyText.gameObject.activeSelf != empty) _emptyText.gameObject.SetActive(empty);

            bool paged = pages > 1;
            if (_prevPage.Root.gameObject.activeSelf != paged) _prevPage.Root.gameObject.SetActive(paged);
            if (_nextPage.Root.gameObject.activeSelf != paged) _nextPage.Root.gameObject.SetActive(paged);
            string pt = empty ? "" : paged ? $"Page {_listPage + 1}/{pages}  ·  {files.Count} scripts" : $"{files.Count} script{(files.Count == 1 ? "" : "s")}";
            if (_pageText.text != pt) _pageText.text = pt;

            if (_feedbackUntil > 0f && Time.unscaledTime > _feedbackUntil)
            {
                _feedbackUntil = 0f;
                _folderFeedback.text = "";
            }
        }

        private void RefreshLabels()
        {
            Teleprompter p = Plugin.Prompter;
            string pill;
            Color pillCol;
            switch (p.State)
            {
                case PlayState.Playing: pill = $"PLAYING {Mathf.RoundToInt(p.Progress * 100f)}%"; pillCol = UiKit.Accent; break;
                case PlayState.Paused: pill = $"PAUSED {Mathf.RoundToInt(p.Progress * 100f)}%"; pillCol = UiKit.Warn; break;
                case PlayState.Ended: pill = "END"; pillCol = UiKit.Stop; break;
                default: pill = "READY"; pillCol = UiKit.TextDim; break;
            }
            if (!p.IsVisible) pill += " · HIDDEN";
            if (_pillText.text != pill) _pillText.text = pill;
            _group.SetColor(_pill, pillCol);

            if (_page != Page.Controls) return;

            _playBtn.SetLabel(p.State == PlayState.Playing ? "PAUSE" : "PLAY");
            _showBtn.SetLabel(p.IsVisible ? "HIDE" : "SHOW");

            string name = Path.GetFileNameWithoutExtension(p.FileName ?? "");
            string line = string.IsNullOrEmpty(p.Error) ? $"Script: {name}  ·  {p.LineCount} lines  ·  change it in CHOOSE SCRIPT" : $"Script: {name}  ·  {p.Error}";
            if (_scriptLine.text != line) _scriptLine.text = line;

            _syncBtn.SetLabel(Plugin.SyncWithRecording.Value ? "ON" : "OFF");
            bool onCam = Plugin.ShowOnCameras.Value;
            _camBtn.SetLabel(onCam ? "VISIBLE" : "HIDDEN");
            string camInfo = onCam ? "Recordings & spectators can see it" : "Only you can see it (headset)";
            if (_camInfo.text != camInfo) _camInfo.text = camInfo;

            string info = Plugin.Recording != null ? Plugin.Recording.StatusText : "";
            if (_syncInfo.text != info) _syncInfo.text = info;
        }

        private void PlaceInFront(bool snap)
        {
            Transform head = Rig.Head;
            if (head == null) return;
            float scale = Rig.Scale;
            Vector3 fwd = head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = head.up; // looking straight down/up
            fwd.y = 0f;
            fwd.Normalize();

            float dist = Plugin.MenuDistance.Value * scale;
            _targetPos = head.position + fwd * dist + Vector3.down * (0.2f * scale);
            Vector3 toPanel = _targetPos - head.position;
            _targetRot = Quaternion.LookRotation(toPanel, Vector3.up);
            if (snap) transform.SetPositionAndRotation(_targetPos, _targetRot);
        }

        private void FollowLazily(float dt)
        {
            Transform head = Rig.Head;
            if (head == null) return;
            float scale = Rig.Scale;
            float d = Vector3.Distance(head.position, _targetPos);
            float far = Mathf.Max(1.3f, Plugin.MenuDistance.Value + 0.7f) * scale;
            if (d > far || d < 0.2f * scale) PlaceInFront(snap: false); // walked away / walked into it
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, _targetPos, UiKit.Damp(8f, dt)),
                Quaternion.Slerp(transform.rotation, _targetRot, UiKit.Damp(8f, dt)));
        }
    }
}
