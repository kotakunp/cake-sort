// Haptic feedback: UIKit feedback generators on iOS, the view's built-in haptic constants on Android
// (no vibrate permission needed), and a no-op elsewhere. Calls are counted so automated runs can check the hooks.
using System.Runtime.InteropServices;
using UnityEngine;

namespace CakeSort.Game
{
    public enum HapticKind { Light, Medium, Heavy, Soft, Rigid, Selection, Success, Warning, Error }

    public static class Haptics
    {
        public static bool Enabled = true;
        public static readonly int[] Counts = new int[9];
        static float _lastTap = -1f;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CakeSortHaptic(int kind);
        [DllImport("__Internal")] static extern void CakeSortHapticPrepare();
#endif

        public static void Play(HapticKind kind)
        {
            if (!Enabled) return;
            // Taps closer together than this blur into a buzz on the Taptic Engine; keep only the first.
            bool tap = kind <= HapticKind.Rigid || kind == HapticKind.Selection;
            if (tap && Time.unscaledTime - _lastTap < 0.045f) return;
            if (tap) _lastTap = Time.unscaledTime;
            Counts[(int)kind]++;
#if UNITY_IOS && !UNITY_EDITOR
            CakeSortHaptic((int)kind);
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidPlay(kind);
#endif
        }

        public static void Prepare()
        {
            if (!Enabled) return;
#if UNITY_IOS && !UNITY_EDITOR
            CakeSortHapticPrepare();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject _view;
        static int _sdk;

        static void AndroidPlay(HapticKind kind)
        {
            try
            {
                if (_view == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    {
                        var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                        _view = activity.Call<AndroidJavaObject>("getWindow").Call<AndroidJavaObject>("getDecorView");
                    }
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION")) _sdk = version.GetStatic<int>("SDK_INT");
                }
                // HapticFeedbackConstants: LONG_PRESS 0, KEYBOARD_TAP 3, CLOCK_TICK 4, CONFIRM 16 and REJECT 17 (API 30+).
                int constant;
                switch (kind)
                {
                    case HapticKind.Light: case HapticKind.Selection: case HapticKind.Soft: constant = 4; break;
                    case HapticKind.Medium: constant = 3; break;
                    case HapticKind.Success: constant = _sdk >= 30 ? 16 : 3; break;
                    case HapticKind.Warning: case HapticKind.Error: constant = _sdk >= 30 ? 17 : 0; break;
                    default: constant = 0; break;
                }
                _view.Call<bool>("performHapticFeedback", constant);
            }
            catch (System.Exception)
            {
                Enabled = false;
            }
        }
#endif
    }
}
