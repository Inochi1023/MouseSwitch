using System;

namespace MouseSwitch
{
    /// <summary>
    /// Decides whether Ctrl and Alt are really held, for the Ctrl+Alt+S and
    /// Ctrl+Alt+K hotkeys. No Windows calls in here, so it can be tested alone.
    ///
    /// Remembering "Ctrl went down, has not come up" is not enough: if a key-up
    /// is ever missed (Ctrl+Alt+Del, the lock screen, an elevated window, a game
    /// overlay or the Korean Han/Eng key can all eat one), the flag stays set and
    /// from then on every plain S - "walk backwards" in most games - looks like
    /// Ctrl+Alt+S.
    ///
    /// Three independent guards, any one of which stops that:
    ///  1. When the keys are reaching Windows normally (they are not being
    ///     forwarded), Windows' own key state is asked and has the final word.
    ///  2. A held key keeps sending auto-repeat key-downs, but only the most
    ///     recently pressed key repeats. So the newer of the two modifiers must
    ///     have been seen within RecentMs - someone is actively holding them.
    ///  3. Neither modifier may be older than MaxHoldMs; a stuck flag is
    ///     typically minutes or hours old and gets thrown away.
    /// </summary>
    internal sealed class ChordDetector
    {
        public const int RecentMs = 1500;
        public const int MaxHoldMs = 10000;

        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;
        public const int VK_LCONTROL = 0xA2;
        public const int VK_RCONTROL = 0xA3;
        public const int VK_LMENU = 0xA4;
        public const int VK_RMENU = 0xA5;

        private bool _ctrl, _alt;
        private int _ctrlTick, _altTick;

        public void OnKey(int vk, bool down, int now)
        {
            if (vk == VK_LCONTROL || vk == VK_RCONTROL || vk == VK_CONTROL)
            {
                _ctrl = down;
                if (down) _ctrlTick = now;
            }
            else if (vk == VK_LMENU || vk == VK_RMENU || vk == VK_MENU)
            {
                _alt = down;
                if (down) _altTick = now;
            }
        }

        /// <param name="now">Environment.TickCount</param>
        /// <param name="systemCtrl">Windows' own view, or null when keys are being
        /// forwarded (then Windows never sees them and cannot be asked)</param>
        /// <param name="systemAlt">same for Alt</param>
        public bool CtrlAltHeld(int now, bool? systemCtrl, bool? systemAlt)
        {
            if (_ctrl && unchecked(now - _ctrlTick) > MaxHoldMs) _ctrl = false;
            if (_alt && unchecked(now - _altTick) > MaxHoldMs) _alt = false;

            if (systemCtrl.HasValue && !systemCtrl.Value) _ctrl = false;
            if (systemAlt.HasValue && !systemAlt.Value) _alt = false;

            if (!_ctrl || !_alt) return false;

            int newest = unchecked(now - _ctrlTick) < unchecked(now - _altTick) ? _ctrlTick : _altTick;
            return unchecked(now - newest) <= RecentMs;
        }
    }
}
