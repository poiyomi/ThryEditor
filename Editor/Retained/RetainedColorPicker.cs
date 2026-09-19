using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal static class RetainedColorPicker
    {
        private static readonly MethodInfo Show = typeof(Editor).Assembly.GetType("UnityEditor.ColorPicker")?.GetMethod(
            "Show", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { typeof(Action<Color>), typeof(Color), typeof(bool), typeof(bool) }, null);

        internal static void Attach(ColorField field)
        {
            // Unity 2022's ColorField uses IMGUI. Its default picker callback sends
            // an ExecuteCommand through the whole Inspector for each color change.
            // Keep Unity's picker and swatch, but deliver edits directly to the field.
            var input = field.Q<IMGUIContainer>();
            if (input == null || Show == null) return; // Newer retained controls and older APIs keep their native path.
            field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || !field.enabledInHierarchy || !input.worldBound.Contains(e.position)) return;
                // The eyedropper is a separate native operation. Let its original
                // handler receive the click, as well as context menus and keyboard input.
                if (field.showEyeDropper && e.position.x >= input.worldBound.xMax - 20) return;
                Action<Color> changed = value =>
                {
                    if (field.panel == null) return;
                    // Escape has already reverted the picker's Undo group. Unity
                    // then reports its single starting swatch; writing that back
                    // would overwrite the restored colors of a mixed selection.
                    if (Event.current != null && (Event.current.commandName == "UndoRedoPerformed"
                        || (Event.current.rawType == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))) return;
                    if (field.showMixedValue && field.value.Equals(value))
                    {
                        // Choosing the first owner's existing color must still apply
                        // it to the other owners of a mixed selection.
                        using (var change = ChangeEvent<Color>.GetPooled(field.value, value))
                        { change.target = field; field.SendEvent(change); }
                    }
                    else field.value = value;
                };
                field.Focus();
                Show.Invoke(null, new object[] { changed, field.value, field.showAlpha, field.hdr });
                e.PreventDefault(); e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
        }
    }
}
