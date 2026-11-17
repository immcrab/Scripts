using System;
using BepInEx;
using UnityEngine;
using UnityEngine.XR;

namespace Scripts
{
    /// <summary>
    /// Rising-edge detection for the menu toggle. Reads the game's own ControllerInputPoller, which maps
    /// SteamVR actions (Index, Quest via Link/SteamVR) and Oculus/OpenXR devices onto the same fields, and
    /// falls back to the raw XR device. Holding the button toggles once; a cooldown absorbs bounce.
    /// </summary>
    internal sealed class MenuInput
    {
        private const float Cooldown = 0.4f;

        private bool _wasDown;
        private float _nextAllowed;
        private bool _keyboardBroken;

        public bool ConsumePress()
        {
            bool down = IsDown();
            bool edge = down && !_wasDown;
            _wasDown = down;

            if (!edge || Time.unscaledTime < _nextAllowed)
                return false;

            _nextAllowed = Time.unscaledTime + Cooldown;
            return true;
        }

        private bool IsDown()
        {
            MenuButton which = Plugin.ToggleButton.Value;
            ControllerInputPoller poller = ControllerInputPoller.instance;
            if (poller != null)
            {
                bool polled;
                switch (which)
                {
                    case MenuButton.RightPrimary: polled = poller.rightControllerPrimaryButton; break;
                    case MenuButton.LeftSecondary: polled = poller.leftControllerSecondaryButton; break;
                    case MenuButton.LeftPrimary: polled = poller.leftControllerPrimaryButton; break;
                    default: polled = poller.rightControllerSecondaryButton; break;
                }
                if (polled) return true;

                bool left = which == MenuButton.LeftPrimary || which == MenuButton.LeftSecondary;
                bool primary = which == MenuButton.LeftPrimary || which == MenuButton.RightPrimary;
                InputDevice dev = left ? poller.leftControllerDevice : poller.rightControllerDevice;
                if (dev.isValid && dev.TryGetFeatureValue(primary ? CommonUsages.primaryButton : CommonUsages.secondaryButton, out bool b) && b)
                    return true;
            }

            if (!_keyboardBroken)
            {
                try
                {
                    KeyCode key = Plugin.ToggleKey.Value.MainKey;
                    if (key != KeyCode.None && UnityInput.Current.GetKey(key))
                        return true;
                }
                catch (Exception e)
                {
                    _keyboardBroken = true;
                    Plugin.Log.LogWarning("Keyboard input unavailable: " + e.Message);
                }
            }

            return false;
        }
    }
}
