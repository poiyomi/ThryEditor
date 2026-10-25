namespace Thry.ThryEditor.Helpers
{
    public static class ObjectExtensions
    {
        /// <summary>
        /// Returns a stable per-session integer id for a Unity object, using the non-obsolete
        /// EntityId API on Unity 6.4+ and falling back to GetInstanceID() on older versions.
        /// </summary>
        public static int GetObjectId(this UnityEngine.Object obj)
        {
            if (obj == null) return 0;
#if UNITY_6000_4_OR_NEWER
            return obj.GetEntityId().GetHashCode();
#else
            return obj.GetInstanceID();
#endif
        }

        /// <summary>
        /// Every loaded object of a type, unsorted, through the FindObjectsByType overload the running Unity has.
        /// </summary>
        public static T[] FindObjects<T>(bool includeInactive) where T : UnityEngine.Object
        {
            var inactive = includeInactive ? UnityEngine.FindObjectsInactive.Include : UnityEngine.FindObjectsInactive.Exclude;
#if UNITY_6000_4_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(inactive);
#else
            return UnityEngine.Object.FindObjectsByType<T>(inactive, UnityEngine.FindObjectsSortMode.None);
#endif
        }
    }
}
