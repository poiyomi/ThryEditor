using System.Collections.Generic;
using System.Reflection;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    /// <summary>
    /// What a color picker or eyedropper session edits: a material color row, a plain color field,
    /// or an IMGUI color control. The picker owns the editable state; the target owns writing,
    /// restoring and undo.
    /// </summary>
    internal interface IColorPickerTarget
    {
        /// <summary>Current stored value (the first owner's when the owners differ).</summary>
        Color Value { get; }
        bool Mixed { get; }
        ThryColorFormat Format { get; }
        /// <summary>Property caption, used for the window title and the undo name.</summary>
        string Title { get; }
        /// <summary>False once the field, material or inspector went away or can no longer be edited.</summary>
        bool IsValid { get; }
        /// <summary>The swatch in screen points, or Rect.zero when unknown.</summary>
        Rect ScreenRect { get; }
        /// <summary>Called once per session: opens the undo group and remembers each owner's value.</summary>
        void Begin();
        /// <summary>Writes a stored value to every owner before returning. Called at most once per frame.</summary>
        void Apply(Color raw);
        /// <summary>Restores every owner to its value from Begin before returning. More Apply calls may follow.</summary>
        void Cancel();
        /// <summary>Ends the session: a cancelled session leaves no undo step, otherwise its writes become one step.</summary>
        void End(bool cancelled);
    }

    /// <summary>
    /// The undo group of one picker session. It stays unnamed: Unity keeps a named group as an undo step even when
    /// nothing was recorded into it, so an owner that records no undo (a draft, a plain field) would get a step that
    /// does nothing. Owners that record name the step themselves, like "Modify Tint of …".
    /// </summary>
    internal sealed class ColorPickerUndoGroup
    {
        static readonly MethodInfo GetRecords = typeof(Undo).GetMethod("GetRecords", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(List<string>), typeof(List<string>) }, null);

        readonly List<string> _before = new List<string>();
        bool _known;
        int _group = -1;

        /// <summary>
        /// True while a session takes back its own records. The undo callback that follows only undoes values the session
        /// wrote, so owners that rebuild their UI on undo can refresh in place and keep an open picker or popup.
        /// </summary>
        internal static bool Reverting { get; private set; }

        internal void Start()
        {
            Undo.IncrementCurrentGroup();
            _group = Undo.GetCurrentGroup();
            _known = Read(_before);
        }

        /// <summary>Anything was recorded since <see cref="Start"/>; true when Unity does not say.</summary>
        internal bool Recorded
        {
            get
            {
                // The whole list is compared, not its length, so an old step Unity trims as a new one arrives still counts.
                var now = new List<string>();
                if (!_known || !Read(now) || now.Count != _before.Count) return true;
                for (int i = 0; i < now.Count; i++) if (now[i] != _before[i]) return true;
                return false;
            }
        }

        /// <summary>
        /// Takes back what was recorded since <see cref="Start"/>. Reverting raises Undo.undoRedoPerformed, which rebuilds
        /// some owners' UI, so nothing happens when there is nothing to take back.
        /// </summary>
        internal void Revert()
        {
            if (!Recorded) return;
            Reverting = true;
            try { Undo.RevertAllDownToGroup(_group); }
            finally { Reverting = false; }
        }

        /// <summary>Makes what was recorded since <see cref="Start"/> one undo step.</summary>
        internal void Commit()
        {
            if (Recorded) Undo.CollapseUndoOperations(_group);
        }

        static bool Read(List<string> records)
        {
            records.Clear();
            if (GetRecords == null) return false;
            try { GetRecords.Invoke(null, new object[] { records, new List<string>() }); return true; }
            catch (TargetInvocationException) { return false; }
            catch (System.ArgumentException) { return false; }
        }
    }
}
