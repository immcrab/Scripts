using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Scripts
{
    /// <summary>
    /// Makes the mod's renderers visible to the headset camera only.
    ///
    /// Gorilla Tag renders with URP, so every camera (the VR eye camera, the desktop third-person Cinemachine
    /// camera, the built-in LIV LCK recording/streaming cameras, mirror cameras and any camera added by other
    /// mods) goes through RenderPipelineManager.beginCameraRendering before it culls. We flip
    /// Renderer.forceRenderingOff for each camera: off for the headset camera, on for everything else. This is
    /// the same per-camera technique the game's own LckHideObjectFromCamera uses. As a second line of defence the
    /// objects are put on the game's "LCKHide" layer (or FirstPersonOnly) when the headset camera renders it.
    ///
    /// Limitation: anything that copies the headset image itself (SteamVR "VR View", Oculus mirror, Quest
    /// casting, OBS capturing the headset mirror window) shows exactly what the headset shows, panel included.
    /// </summary>
    internal static class CameraHider
    {
        public const int LayerLckHide = 17;      // UnityLayer.LCKHide
        public const int LayerFirstPersonOnly = 19; // UnityLayer.FirstPersonOnly

        private static readonly List<Renderer> _renderers = new List<Renderer>();
        private static readonly HashSet<Renderer> _known = new HashSet<Renderer>();
        private static readonly List<GameObject> _roots = new List<GameObject>();
        private static float _nextRescan;
        private static bool _installed;
        private static bool _hiddenState;
        private static Camera _vrCamera;
        private static int _layer = -1;

        public static Camera VrCamera
        {
            get
            {
                if (_vrCamera != null && _vrCamera.isActiveAndEnabled) return _vrCamera;
                _vrCamera = FindVrCamera();
                if (_vrCamera != null) RefreshLayer();
                return _vrCamera;
            }
        }

        public static void Install()
        {
            if (_installed) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            Camera.onPreCull += OnPreCullLegacy; // only fires under the built-in pipeline; harmless otherwise
            Plugin.ShowOnCameras.SettingChanged += (_, __) =>
            {
                RefreshLayer();
                Plugin.Log.LogInfo(Plugin.ShowOnCameras.Value
                    ? "Panels are now visible to recording/spectator cameras."
                    : "Panels are now hidden from recording/spectator cameras.");
            };
            _installed = true;
            Plugin.Log.LogInfo($"Camera hider installed. Headset camera: {(VrCamera != null ? VrCamera.name : "not found yet")}, layer {Layer}.");
        }

        public static void Uninstall()
        {
            if (!_installed) return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Camera.onPreCull -= OnPreCullLegacy;
            _installed = false;
        }

        public static int Layer => _layer >= 0 ? _layer : 5;

        public static void Register(GameObject root)
        {
            if (!_roots.Contains(root)) _roots.Add(root);
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!_known.Add(r)) continue;
                _renderers.Add(r);
                r.gameObject.layer = Layer;
                r.forceRenderingOff = _hiddenState;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        /// <summary>
        /// TextMeshPro creates sub-mesh renderers at runtime for glyphs from fallback fonts; pick those up too.
        /// </summary>
        public static void Rescan()
        {
            if (Time.unscaledTime < _nextRescan) return;
            _nextRescan = Time.unscaledTime + 0.5f;
            _known.RemoveWhere(r => r == null);
            _renderers.RemoveAll(r => r == null);
            _roots.RemoveAll(g => g == null);
            foreach (GameObject g in _roots) Register(g);
        }

        private static Camera FindVrCamera()
        {
            try
            {
                if (GTFirstPersonCamera.camera != null) return GTFirstPersonCamera.camera;
            }
            catch
            {
                // Type exists in current builds; guard against future renames.
            }

            GorillaTagger t = GorillaTagger.Instance;
            if (t != null && t.mainCamera != null)
            {
                Camera c = t.mainCamera.GetComponent<Camera>();
                if (c != null) return c;
            }
            return null;
        }

        public static void RefreshLayer()
        {
            if (_vrCamera == null) return;
            int mask = _vrCamera.cullingMask;
            int layer;
            if (Plugin.ShowOnCameras.Value) layer = 0; // Default: drawn by the headset, desktop, LCK and mod cameras
            else if ((mask & (1 << LayerLckHide)) != 0) layer = LayerLckHide;
            else if ((mask & (1 << LayerFirstPersonOnly)) != 0) layer = LayerFirstPersonOnly;
            else layer = 5; // UI; per-camera hiding still applies
            if (layer == _layer) return;

            _layer = layer;
            _renderers.RemoveAll(r => r == null);
            foreach (Renderer r in _renderers) r.gameObject.layer = layer;
            Plugin.Log.LogInfo($"Teleprompter render layer: {layer} ({LayerMask.LayerToName(layer)})");
        }

        private static bool ShouldShow(Camera cam)
        {
            if (Plugin.ShowOnCameras.Value) return true; // user opted in: every camera may draw the panel
            Camera vr = VrCamera;
            if (vr != null) return cam == vr;
            return cam.stereoEnabled; // unknown setup: only headset-rendering cameras
        }

        private static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam) => Apply(cam);

        private static void OnPreCullLegacy(Camera cam) => Apply(cam);

        private static void Apply(Camera cam)
        {
            if (cam == null) return;
            bool hide = !ShouldShow(cam);
            if (hide == _hiddenState) return;
            _hiddenState = hide;

            for (int i = _renderers.Count - 1; i >= 0; i--)
            {
                Renderer r = _renderers[i];
                if (r == null) { _renderers.RemoveAt(i); continue; }
                r.forceRenderingOff = hide;
            }
        }
    }
}
