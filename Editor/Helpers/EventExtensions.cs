using UnityEngine.UIElements;

namespace Thry.ThryEditor.Helpers
{
    public static class EventExtensions
    {
        /// <summary>
        /// PreventDefault without the obsolete call: on Unity 6 it stops propagation and tells the
        /// focus controller to ignore the event, which is what PreventDefault does there.
        /// </summary>
        public static void PreventDefaultHandling(this EventBase e)
        {
#if UNITY_6000_0_OR_NEWER
            e.StopPropagation();
            (e.target as VisualElement)?.focusController?.IgnoreEvent(e);
#else
            e.PreventDefault();
#endif
        }

        /// <summary>
        /// isDefaultPrevented without the obsolete call; on Unity 6 it reports isPropagationStopped.
        /// </summary>
        public static bool IsHandled(this EventBase e)
        {
#if UNITY_6000_0_OR_NEWER
            return e.isPropagationStopped;
#else
            return e.isDefaultPrevented;
#endif
        }
    }
}
