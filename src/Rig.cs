using GorillaLocomotion;
using UnityEngine;

namespace Scripts
{
    /// <summary>Read-only access to the local player's head, body and fingertips.</summary>
    internal static class Rig
    {
        /// <summary>The VR eye camera when available (exact eye pose), otherwise the head collider.</summary>
        public static Transform Head
        {
            get
            {
                Camera cam = CameraHider.VrCamera;
                if (cam != null) return cam.transform;
                GTPlayer p = GTPlayer.Instance;
                return p != null && p.headCollider != null ? p.headCollider.transform : null;
            }
        }

        public static Transform Body
        {
            get
            {
                GTPlayer p = GTPlayer.Instance;
                return p != null && p.bodyCollider != null ? p.bodyCollider.transform : null;
            }
        }

        public static float Scale
        {
            get
            {
                GTPlayer p = GTPlayer.Instance;
                float s = p != null ? p.scale : 1f;
                return s > 0.01f ? s : 1f;
            }
        }

        /// <summary>Index fingertip trigger objects the game itself uses for pressing in-world buttons.</summary>
        public static bool TryFingertip(bool left, out Vector3 pos)
        {
            GorillaTagger t = GorillaTagger.Instance;
            if (t != null)
            {
                GameObject tip = left ? t.leftHandTriggerCollider : t.rightHandTriggerCollider;
                if (tip != null)
                {
                    pos = tip.transform.position;
                    return true;
                }
                Transform hand = left ? t.leftHandTransform : t.rightHandTransform;
                if (hand != null)
                {
                    pos = hand.position;
                    return true;
                }
            }
            pos = default;
            return false;
        }

        public static void Haptic(bool left, float amplitude, float duration)
        {
            try
            {
                GorillaTagger t = GorillaTagger.Instance;
                if (t != null) t.StartVibration(left, amplitude, duration);
            }
            catch
            {
                // Haptics are cosmetic.
            }
        }
    }
}
