using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GorillaLocomotion;
using UnityEngine;

namespace Scripts
{
    internal static class PluginInfo
    {
        public const string Guid = "com.scripts.gorillatag.teleprompter";
        public const string Name = "Scripts";
        public const string Version = "1.0.0";
    }

    public enum FollowMode { Head, Body }

    public enum MenuButton
    {
        RightSecondary, // B (Quest Touch / Index), mapped through the game's own input poller
        RightPrimary,   // A
        LeftSecondary,  // Y
        LeftPrimary     // X
    }

    /// <summary>
    /// Entry point. Owns config, the script library, the teleprompter, the settings menu and the optional
    /// recording sync. Everything is local: no networking, no RPCs, nothing touches other players.
    /// </summary>
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        // Teleprompter
        internal static ConfigEntry<float> Opacity, TextSize, Distance, Speed, Smoothing, VerticalOffset, PanelWidth, PanelHeight, ReadingLine, MenuDistance;
        internal static ConfigEntry<FollowMode> Follow;
        internal static ConfigEntry<bool> Visible;
        internal static ConfigEntry<string> ActiveScript;
        // Recording
        internal static ConfigEntry<bool> SyncWithRecording, ShowOnCameras;
        // Input
        internal static ConfigEntry<MenuButton> ToggleButton;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;

        internal static ScriptLibrary Library { get; private set; }
        internal static Teleprompter Prompter { get; private set; }
        internal static SettingsMenu Menu { get; private set; }
        internal static RecordingSync Recording { get; private set; }

        private MenuInput _input;
        private bool _built;
        private float _nextBuildTry;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Opacity = Config.Bind("Teleprompter", "Opacity", 0.75f,
                new ConfigDescription("Background opacity of the teleprompter panel.", new AcceptableValueRange<float>(0.1f, 1f)));
            TextSize = Config.Bind("Teleprompter", "TextSize", 44f,
                new ConfigDescription("Script text size.", new AcceptableValueRange<float>(20f, 100f)));
            Distance = Config.Bind("Teleprompter", "Distance", 1.2f,
                new ConfigDescription("Distance from your eyes to the panel, in meters (scaled with player size).", new AcceptableValueRange<float>(0.4f, 3f)));
            Speed = Config.Bind("Teleprompter", "Speed", 0.5f,
                new ConfigDescription("Scrolling speed in lines per second.", new AcceptableValueRange<float>(0.05f, 3f)));
            Follow = Config.Bind("Teleprompter", "FollowMode", FollowMode.Head,
                "Head: the panel stays in front of your view. Body: the panel stays in front of your body and only re-centers when you turn away.");
            Smoothing = Config.Bind("Teleprompter", "FollowSmoothing", 5f,
                new ConfigDescription("How quickly the panel catches up with you. Lower = smoother/slower.", new AcceptableValueRange<float>(1f, 20f)));
            VerticalOffset = Config.Bind("Teleprompter", "VerticalOffset", -0.05f,
                new ConfigDescription("Panel height relative to eye level, in meters.", new AcceptableValueRange<float>(-0.6f, 0.6f)));
            PanelWidth = Config.Bind("Teleprompter", "PanelWidth", 0.9f,
                new ConfigDescription("Panel width in meters.", new AcceptableValueRange<float>(0.4f, 1.8f)));
            PanelHeight = Config.Bind("Teleprompter", "PanelHeight", 0.5f,
                new ConfigDescription("Panel height in meters.", new AcceptableValueRange<float>(0.25f, 1.2f)));
            ReadingLine = Config.Bind("Teleprompter", "ReadingLine", 0.35f,
                new ConfigDescription("Where the reading cue sits, as a fraction of the text area from the top.", new AcceptableValueRange<float>(0.1f, 0.7f)));
            Visible = Config.Bind("Teleprompter", "Visible", false,
                "Whether the teleprompter panel is shown. Always reset to hidden when the game starts; show it with SHOW or PLAY in the menu.");
            Visible.Value = false; // start hidden every launch
            ActiveScript = Config.Bind("Teleprompter", "ActiveScript", "",
                "File name of the script in BepInEx/config/Scripts to show. Empty = first file alphabetically.");

            SyncWithRecording = Config.Bind("Recording", "SyncWithRecording", false,
                "Start the script from the top when the built-in LCK camera starts recording; pause when it pauses or stops.");

            ShowOnCameras = Config.Bind("Recording", "ShowOnCameras", false,
                "false (default) = the teleprompter and menu are only visible in your headset and hidden from the desktop view, the in-game LCK camera and other cameras. true = every camera can see them.");

            MenuDistance = Config.Bind("Input", "MenuDistance", 0.6f,
                new ConfigDescription("How far in front of you the settings menu opens, in meters (scaled with player size).", new AcceptableValueRange<float>(0.3f, 1.2f)));

            ToggleButton = Config.Bind("Input", "MenuButton", MenuButton.RightSecondary,
                "Controller button that opens/closes the settings menu. RightSecondary = B on the right controller.");
            ToggleKey = Config.Bind("Input", "MenuKey", new KeyboardShortcut(KeyCode.F6),
                "Keyboard key that also toggles the settings menu (desktop testing).");

            Library = new ScriptLibrary(System.IO.Path.Combine(Paths.ConfigPath, "Scripts"));
            Library.EnsureFolder();
            _input = new MenuInput();

            Log.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded. Scripts folder: {Library.Folder}");
        }

        private void Update()
        {
            try
            {
                if (!_built)
                {
                    TryBuild();
                    return;
                }

                Recording?.Tick();
                CameraHider.Rescan();

                if (_input.ConsumePress())
                    Menu.Toggle();
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }

        /// <summary>The UI needs the local player, the VR camera and a TMP font, none of which exist at Awake.</summary>
        private void TryBuild()
        {
            if (Time.unscaledTime < _nextBuildTry) return;
            _nextBuildTry = Time.unscaledTime + 1f;

            if (GTPlayer.Instance == null || GorillaTagger.Instance == null || Rig.Head == null) return;
            if (!UiKit.Init()) return;

            CameraHider.Install();

            var root = new GameObject("Scripts.Teleprompter");
            DontDestroyOnLoad(root);
            Prompter = root.AddComponent<Teleprompter>();

            var menuRoot = new GameObject("Scripts.Menu");
            DontDestroyOnLoad(menuRoot);
            Menu = menuRoot.AddComponent<SettingsMenu>();

            Recording = new RecordingSync();

            Library.Refresh();
            Prompter.LoadScript(Library.Load(ActiveScript.Value), keepPosition: false);

            _built = true;
            Log.LogInfo("Scripts UI ready.");
        }

        private void OnDestroy()
        {
            CameraHider.Uninstall();
            Recording?.Dispose();
        }
    }
}
