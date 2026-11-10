using System;
using System.Collections.Generic;
using UnityEngine;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Extra buttons for the inspector's top bar.
    ///
    /// Packages built on this UI can't be referenced from here — the dependency only runs the other
    /// way — so they register their buttons instead, usually from an [InitializeOnLoad] static
    /// constructor. Registered buttons come after the built-in ones.
    /// </summary>
    public static class TopBarButtons
    {
        public class Button
        {
            /// <summary>
            /// Resolved when the toolbar is built rather than stored, so registering can't load
            /// textures before Unity's GUI is ready.
            /// </summary>
            public Func<GUIStyle> Icon;

            public string Tooltip;
            public Action OnClick;
        }

        static readonly List<Button> _buttons = new List<Button>();

        internal static List<Button> All => _buttons;

        /// <summary>
        /// Adds a button to the top bar. Registering again with the same tooltip replaces the
        /// previous entry, so this is safe to call from a static constructor on every domain reload.
        /// </summary>
        public static void Register(Func<GUIStyle> icon, string tooltip, Action onClick)
        {
            if (icon == null || onClick == null)
                return;

            _buttons.RemoveAll(x => x.Tooltip == tooltip);
            _buttons.Add(new Button { Icon = icon, Tooltip = tooltip, OnClick = onClick });
        }

        public static void Unregister(string tooltip) => _buttons.RemoveAll(x => x.Tooltip == tooltip);
    }
}
