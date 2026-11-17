using System;
using TMPro;
using UnityEngine;

namespace Scripts
{
    /// <summary>
    /// A finger-pressed button on the menu panel. No colliders (nothing for the game's physics or other players
    /// to interact with): the fingertip position is tested against the button's box in panel space.
    ///
    /// A press only counts when the fingertip enters from the front, then the button locks until the finger
    /// leaves the zone, with per-button and menu-wide cooldowns, so sweeping a hand across the panel or resting
    /// a finger on a button never fires repeated presses.
    /// </summary>
    internal sealed class VrButton
    {
        private const float Depth = 0.022f;        // how far behind the face the press zone extends
        private const float FrontReach = 0.012f;   // how far in front of the face the zone starts
        private const float Margin = 0.006f;
        private const float Cooldown = 0.3f;

        public readonly Transform Root;
        public readonly MeshRenderer Face;
        public readonly TextMeshPro Label;
        public Action OnPress;
        public Func<bool> IsActive;  // toggled / selected look
        public Color ActiveColor = UiKit.ButtonActive;
        public string Name => Root.name;

        private readonly UiGroup _group;
        private readonly Vector2 _size;
        private readonly Vector3 _rest;
        private readonly bool[] _armed = { true, true };
        private readonly bool[] _wasFront = { false, false };
        private float _nextAllowed;
        private float _pressAnim;   // 1 right after a press, decays to 0
        private float _hover;

        public VrButton(Transform parent, UiGroup group, string name, string label, Vector2 size, Vector3 localPos, float fontSize = 20f)
        {
            _group = group;
            _size = size;
            Root = new GameObject(name).transform;
            Root.SetParent(parent, false);
            Root.localPosition = localPos;
            _rest = localPos;

            Face = UiKit.MakeShape(Root, "Face", UiKit.RoundedRect(size.x, size.y, Mathf.Min(size.x, size.y) * 0.28f), UiKit.Button, 2, false);
            group.Add(Face, UiKit.Button);

            Label = UiKit.MakeText(Root, "Label", label, fontSize, UiKit.Text, TextAlignmentOptions.Center, size, 4, false);
            Label.textWrappingMode = TextWrappingModes.NoWrap;
            Label.fontStyle = FontStyles.Bold;
            Label.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            group.Add(Label, UiKit.Text);
        }

        /// <summary>Returns true if this button fired this frame.</summary>
        public bool Tick(Transform panel, float dt, bool inputEnabled, ref float menuCooldownUntil)
        {
            if (!Root.gameObject.activeInHierarchy) return false; // on a hidden tab / unused list row
            bool fired = false;
            float hoverTarget = 0f;

            for (int hand = 0; hand < 2; hand++)
            {
                bool left = hand == 0;
                if (!Rig.TryFingertip(left, out Vector3 world)) continue;

                // Fingertip in button space (panel scale is the player scale, so convert back to panel meters).
                Vector3 p = panel.InverseTransformPoint(world) - _rest;
                bool inXY = Mathf.Abs(p.x) <= _size.x * 0.5f + Margin && Mathf.Abs(p.y) <= _size.y * 0.5f + Margin;
                bool inFront = p.z < -FrontReach;
                bool inZone = inXY && p.z >= -FrontReach && p.z <= Depth;

                if (inXY && p.z > -0.06f && p.z < Depth) hoverTarget = 1f;

                if (!inZone)
                {
                    // Re-arm only once the finger has clearly left (pulled back or slid off the side).
                    if (!inXY || p.z < -FrontReach - 0.006f || p.z > Depth + 0.02f) _armed[hand] = true;
                }
                else if (_armed[hand] && _wasFront[hand] && inputEnabled
                         && Time.unscaledTime >= _nextAllowed && Time.unscaledTime >= menuCooldownUntil)
                {
                    _armed[hand] = false;
                    _nextAllowed = Time.unscaledTime + Cooldown;
                    menuCooldownUntil = Time.unscaledTime + 0.15f;
                    _pressAnim = 1f;
                    fired = true;
                    Rig.Haptic(left, 0.35f, 0.035f);
                    try { OnPress?.Invoke(); }
                    catch (Exception e) { Plugin.Log.LogError($"Button {Name}: {e}"); }
                }
                else if (inZone)
                {
                    _armed[hand] = false; // entered from the side/back or during cooldown: wait for exit
                }

                // "Approaching from the front": last frame the tip was in front of the face and roughly over the button.
                bool near = Mathf.Abs(p.x) <= _size.x * 0.5f + 0.03f && Mathf.Abs(p.y) <= _size.y * 0.5f + 0.03f;
                _wasFront[hand] = inFront && near;
            }

            // Visuals: hover brighten, press flash + depress, eased back.
            _hover = Mathf.Lerp(_hover, hoverTarget, UiKit.Damp(18f, dt));
            _pressAnim = Mathf.MoveTowards(_pressAnim, 0f, dt / 0.18f);

            bool active = IsActive != null && IsActive();
            Color baseCol = active ? ActiveColor : Color.Lerp(UiKit.Button, UiKit.ButtonHover, _hover);
            Color flash = Color.Lerp(baseCol, Color.white, 0.35f * _pressAnim);
            _group.SetColor(Face, flash);

            float push = UiKit.EaseOutCubic(_pressAnim) * 0.004f;
            Root.localPosition = _rest + new Vector3(0f, 0f, push);
            float s = 1f - 0.04f * _pressAnim;
            Root.localScale = new Vector3(s, s, 1f);
            return fired;
        }

        public void ResetInput()
        {
            _armed[0] = _armed[1] = false; // a finger already inside when the menu opens must leave first
            _wasFront[0] = _wasFront[1] = false;
            _pressAnim = 0f;
        }

        public void SetLabel(string text)
        {
            if (Label.text != text) Label.text = text;
        }
    }
}
