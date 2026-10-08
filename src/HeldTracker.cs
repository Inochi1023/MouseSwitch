using System;
using System.Collections.Generic;

namespace MouseSwitch
{
    /// <summary>
    /// Keeps track of which keys and mouse buttons are physically held, and
    /// which of them were held at the moment control moved to the other PC.
    ///
    /// A key held across a switch is "orphaned": its press already went to the
    /// old side, so its auto-repeats and its release must not reach the new
    /// side, otherwise the old side never gets the release (the key stays
    /// stuck there) and the new side starts receiving a key nobody pressed
    /// there. Orphaned input is swallowed until it is let go.
    ///
    /// No Windows calls in here, so it can be tested alone. Thread safe.
    /// </summary>
    internal sealed class HeldTracker
    {
        // vk -> scan | 0x10000 when extended
        private readonly Dictionary<int, int> _keysDown = new Dictionary<int, int>();
        private readonly HashSet<byte> _buttonsDown = new HashSet<byte>();
        private readonly HashSet<int> _keysOrphaned = new HashSet<int>();
        private readonly HashSet<byte> _buttonsOrphaned = new HashSet<byte>();
        private readonly object _lock = new object();

        /// <returns>true when this event must be swallowed</returns>
        public bool OnKey(int vk, int code, bool down)
        {
            lock (_lock)
            {
                if (down) _keysDown[vk] = code;
                else _keysDown.Remove(vk);

                if (!_keysOrphaned.Contains(vk)) return false;
                if (!down) _keysOrphaned.Remove(vk);
                return true;
            }
        }

        /// <returns>true when this event must be swallowed</returns>
        public bool OnButton(byte button, bool down)
        {
            lock (_lock)
            {
                if (down) _buttonsDown.Add(button);
                else _buttonsDown.Remove(button);

                if (!_buttonsOrphaned.Contains(button)) return false;
                if (!down) _buttonsOrphaned.Remove(button);
                return true;
            }
        }

        /// <summary>For a key whose press was swallowed (a hotkey).</summary>
        public void OrphanKey(int vk)
        {
            lock (_lock) _keysOrphaned.Add(vk);
        }

        /// <summary>
        /// Called when keyboard and/or mouse change destination.
        /// *LeftHere: they used to go to this PC, so this PC must be sent the
        /// releases it will otherwise never get; those are added to keyUps as
        /// (vk, code) pairs and to buttonUps.
        /// </summary>
        public void HandOff(bool keysMoved, bool keysLeftHere, bool mouseMoved, bool mouseLeftHere,
                            List<int> keyUps, List<byte> buttonUps)
        {
            lock (_lock)
            {
                if (keysMoved)
                {
                    foreach (KeyValuePair<int, int> kv in _keysDown)
                    {
                        _keysOrphaned.Add(kv.Key);
                        if (keysLeftHere)
                        {
                            keyUps.Add(kv.Key);
                            keyUps.Add(kv.Value);
                        }
                    }
                }
                if (mouseMoved)
                {
                    foreach (byte b in _buttonsDown)
                    {
                        _buttonsOrphaned.Add(b);
                        if (mouseLeftHere) buttonUps.Add(b);
                    }
                }
            }
        }
    }
}
