using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Unity's color picker swatches: the ColorPresetLibrary (.colors) files, read and written through the
    /// same PresetLibraryManager cache that UnityEditor.ColorPicker uses, so both pickers edit the same objects.
    /// Swatch colors are raw values, as Unity stores them: linear HDR from HDR pickers, the field's own value otherwise.
    /// Every member returns empty or false when Unity's internals are missing.
    /// </summary>
    internal static class UnitySwatchLibrary
    {
        internal struct Swatch
        {
            public Color Color;
            public string Name;
        }

        internal struct LibraryInfo
        {
            public string Path;        // with extension, exactly as Unity lists it (compare with CurrentLibraryPath + ".colors")
            public string Name;        // file name without extension
            public bool InProject;     // Assets/**/Editor/*.colors, shown by Unity as "Name (Project)"
        }

        const string Extension = "colors";
        const string CurrentLibSuffix = "CurrentLib";   // PresetLibraryEditorState: prefix + "CurrentLib"
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly Type PickerType, LibraryType, PresetLibraryType, ManagerType, LocationsType, StateType;
        static readonly object Helper;                   // ScriptableObjectSaveLoadHelper<ColorPresetLibrary>("colors", SaveType.Text)
        static readonly PropertyInfo ManagerInstance, PrefIdProperty, DefaultPathProperty, PickerLibraryProperty;
        static readonly FieldInfo PickerInstanceField, PickerStateField, StateLibraryField;
        static readonly MethodInfo GetLibraryMethod, SaveLibraryMethod, CreateLibraryMethod, AvailableMethod, UnloadMethod;
        static readonly MethodInfo CountMethod, GetPresetMethod, GetNameMethod, AddMethod, RemoveMethod, ReplaceMethod, MoveMethod;

        static readonly object[] s_One = new object[1], s_Two = new object[2], s_Three = new object[3];

        static UnitySwatchLibrary()
        {
            try
            {
                var assembly = typeof(Editor).Assembly;
                PickerType = assembly.GetType("UnityEditor.ColorPicker");
                LibraryType = assembly.GetType("UnityEditor.ColorPresetLibrary");
                PresetLibraryType = assembly.GetType("UnityEditor.PresetLibrary");
                ManagerType = assembly.GetType("UnityEditor.PresetLibraryManager");
                LocationsType = assembly.GetType("UnityEditor.PresetLibraryLocations");
                StateType = assembly.GetType("UnityEditor.PresetLibraryEditorState");
                var helperType = assembly.GetType("UnityEditor.ScriptableObjectSaveLoadHelper`1");

                if (PickerType != null)
                {
                    PrefIdProperty = PickerType.GetProperty("presetsEditorPrefID", AnyStatic);
                    PickerInstanceField = PickerType.GetField("s_Instance", AnyStatic);
                    PickerStateField = PickerType.GetField("m_ColorLibraryEditorState", AnyInstance);
                    PickerLibraryProperty = PickerType.GetProperty("currentPresetLibrary", AnyInstance);
                }
                if (StateType != null) StateLibraryField = StateType.GetField("m_CurrrentLibrary", AnyInstance);   // sic, three r's
                if (LocationsType != null) DefaultPathProperty = LocationsType.GetProperty("defaultPresetLibraryPath", AnyStatic);

                if (LibraryType != null && helperType != null)
                    Helper = Activator.CreateInstance(helperType.MakeGenericType(LibraryType), Extension, SaveType.Text);

                if (ManagerType != null && LibraryType != null)
                {
                    // ScriptableSingleton<PresetLibraryManager>.instance
                    ManagerInstance = ManagerType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                    GetLibraryMethod = Generic(FindMethod(ManagerType, "GetLibrary", 2, AnyInstance));
                    SaveLibraryMethod = Generic(FindMethod(ManagerType, "SaveLibrary", 3, AnyInstance));
                    CreateLibraryMethod = Generic(FindMethod(ManagerType, "CreateLibrary", 2, AnyInstance));
                    AvailableMethod = Generic(FindMethod(ManagerType, "GetAvailableLibraries", 3, AnyInstance));
                    UnloadMethod = Generic(FindMethod(ManagerType, "UnloadAllLibrariesFor", 1, AnyInstance));
                }

                if (PresetLibraryType != null)
                {
                    CountMethod = FindMethod(PresetLibraryType, "Count", 0, AnyInstance);
                    GetPresetMethod = FindMethod(PresetLibraryType, "GetPreset", 1, AnyInstance);
                    GetNameMethod = FindMethod(PresetLibraryType, "GetName", 1, AnyInstance);
                    AddMethod = FindMethod(PresetLibraryType, "Add", 2, AnyInstance);
                    RemoveMethod = FindMethod(PresetLibraryType, "Remove", 1, AnyInstance);
                    ReplaceMethod = FindMethod(PresetLibraryType, "Replace", 2, AnyInstance);
                    MoveMethod = FindMethod(PresetLibraryType, "Move", 3, AnyInstance);
                }
            }
            catch (Exception)
            {
                Helper = null;   // Any surprise in Unity's internals: Available becomes false instead of a TypeInitializationException.
            }
        }

        static MethodInfo FindMethod(Type type, string name, int parameterCount, BindingFlags flags)
        {
            var methods = type.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
                if (methods[i].Name == name && methods[i].GetParameters().Length == parameterCount) return methods[i];
            return null;
        }

        static MethodInfo Generic(MethodInfo method)
        {
            if (method == null || !method.IsGenericMethodDefinition || method.GetGenericArguments().Length != 1) return null;
            return method.MakeGenericMethod(LibraryType);
        }

        /// <summary>Loading, listing and editing swatches works in this editor.</summary>
        internal static bool Available => Helper != null && ManagerInstance != null && GetLibraryMethod != null
            && SaveLibraryMethod != null && CountMethod != null && GetPresetMethod != null && GetNameMethod != null
            && AddMethod != null && RemoveMethod != null;

        /// <summary>Listing and choosing libraries also works.</summary>
        internal static bool LibrariesAvailable => Available && AvailableMethod != null;

        static object Manager
        {
            get
            {
                if (ManagerInstance == null) return null;
                try { return ManagerInstance.GetValue(null); }
                catch (TargetInvocationException) { return null; }
            }
        }

        // Static methods pass target null; instance methods need a target.
        static bool TryCall(MethodInfo method, object target, object[] arguments, out object result)
        {
            result = null;
            if (method == null || (target == null && !method.IsStatic)) return false;
            try { result = method.Invoke(target, arguments); return true; }
            catch (TargetInvocationException) { return false; }
            catch (ArgumentException) { return false; }
            catch (TargetException) { return false; }
            catch (MemberAccessException) { return false; }
        }

        static object Invoke(MethodInfo method, object target, object[] arguments)
            => TryCall(method, target, arguments, out var result) ? result : null;

        static bool TryInvoke(MethodInfo method, object target, object[] arguments)
            => TryCall(method, target, arguments, out _);

        // ---- Where the current library is -------------------------------------------------------------

        /// <summary>The EditorPrefs key Unity's picker keeps its library in: ColorPicker.presetsEditorPrefID + "CurrentLib" ("ColorCurrentLib").</summary>
        internal static string PrefKey
        {
            get
            {
                string prefix = null;
                try { prefix = PrefIdProperty?.GetValue(null) as string; }
                catch (TargetInvocationException) { }
                return (string.IsNullOrEmpty(prefix) ? "Color" : prefix) + CurrentLibSuffix;
            }
        }

        /// <summary>PresetLibraryLocations.defaultPresetLibraryPath: the Preferences folder's Presets/Default, without extension.</summary>
        internal static string DefaultLibraryPath
        {
            get
            {
                string path = null;
                try { path = DefaultPathProperty?.GetValue(null) as string; }
                catch (TargetInvocationException) { }
                return string.IsNullOrEmpty(path) ? Path.Combine(InternalEditorUtility.unityPreferencesFolder + "/Presets/", "Default") : path;
            }
        }

        /// <summary>Unity's picker window, only if one exists. Never creates one (ColorPicker.instance would).</summary>
        static UnityEngine.Object OpenPicker
        {
            get
            {
                if (PickerInstanceField == null) return null;
                var picker = PickerInstanceField.GetValue(null) as UnityEngine.Object;
                return picker != null ? picker : null;
            }
        }

        /// <summary>
        /// The library Unity's picker shows, without extension, resolved like ColorPicker: the open picker's live state
        /// (Unity writes EditorPrefs only when its picker closes), else EditorPrefs "ColorCurrentLib", else the Default library.
        /// It may name a file that no longer exists; <see cref="Read"/> and the edits then fall back to Default, as Unity does.
        /// </summary>
        internal static string CurrentLibraryPath
        {
            get
            {
                var picker = OpenPicker;
                if (picker != null && PickerStateField != null && StateLibraryField != null)
                {
                    var state = PickerStateField.GetValue(picker);
                    if (state != null && StateLibraryField.GetValue(state) is string live && live.Length > 0) return live;
                }
                return EditorPrefs.GetString(PrefKey, DefaultLibraryPath);
            }
        }

        static bool IsInPreferences(string path) => path.Contains(InternalEditorUtility.unityPreferencesFolder);

        // PresetLibraryLocations.GetFileLocationFromPath, without its LogError for other folders.
        static bool IsInProject(string path) => !IsInPreferences(path) && path.Contains("Assets/");

        static ScriptableObject Load(string pathWithoutExtension)
        {
            var manager = Manager;
            if (manager == null || string.IsNullOrEmpty(pathWithoutExtension)) return null;
            s_Two[0] = Helper; s_Two[1] = pathWithoutExtension;
            // A cached library destroyed behind the manager's back makes GetLibrary drop it, then throw while logging
            // (it reads the removed index). The retry loads it from disk again.
            if (!TryCall(GetLibraryMethod, manager, s_Two, out var result)) TryCall(GetLibraryMethod, manager, s_Two, out result);
            s_Two[0] = null; s_Two[1] = null;
            var library = result as ScriptableObject;
            return library != null ? library : null;
        }

        /// <summary>PresetLibraryEditor.GetCurrentLib: the current library, else Default, else (create) a new empty Default.</summary>
        static ScriptableObject Resolve(bool create, out string path)
        {
            path = CurrentLibraryPath;
            var library = Load(path);
            if (library != null) return library;

            string fallback = DefaultLibraryPath;
            library = fallback == path ? null : Load(fallback);
            if (library == null && create) library = CreateAndSave(fallback);
            if (library == null) { path = ""; return null; }
            path = fallback;
            // Unity switches its state to Default here and stores that when its picker closes.
            if (create) SetCurrentLibrary(fallback);
            return library;
        }

        static ScriptableObject CreateAndSave(string pathWithoutExtension)
        {
            var manager = Manager;
            if (manager == null || CreateLibraryMethod == null) return null;
            s_Two[0] = Helper; s_Two[1] = pathWithoutExtension;
            var library = Invoke(CreateLibraryMethod, manager, s_Two) as ScriptableObject;
            s_Two[0] = null; s_Two[1] = null;
            if (library == null) return null;
            if (!Save(library, pathWithoutExtension)) return null;
            return library;
        }

        static bool Save(ScriptableObject library, string pathWithoutExtension)
        {
            var manager = Manager;
            if (manager == null || library == null) return false;
            s_Three[0] = Helper; s_Three[1] = library; s_Three[2] = pathWithoutExtension;
            bool ok = TryInvoke(SaveLibraryMethod, manager, s_Three);   // writes the file; AssetDatabase.Refresh() if it is new
            s_Three[0] = null; s_Three[1] = null; s_Three[2] = null;
            if (ok) InternalEditorUtility.RepaintAllViews();   // PresetLibraryEditor.SaveCurrentLib; repaints Unity's picker too
            return ok;
        }

        static int Count(ScriptableObject library) => Invoke(CountMethod, library, null) is int count ? count : 0;

        // ---- Reading ------------------------------------------------------------------------------

        /// <summary>
        /// Fills <paramref name="into"/> with the current library's swatches. Loads the file into Unity's cache if needed,
        /// never creates one. <paramref name="libraryPath"/> is the library read, without extension, or "" if none loaded.
        /// </summary>
        internal static bool Read(List<Swatch> into, out string libraryPath)
        {
            into.Clear();
            libraryPath = "";
            if (!Available) return false;
            var library = Resolve(false, out libraryPath);
            if (library == null) return false;
            int count = Count(library);
            if (into.Capacity < count) into.Capacity = count;
            for (int i = 0; i < count; i++)
            {
                s_One[0] = i;
                var color = Invoke(GetPresetMethod, library, s_One);
                s_One[0] = i;
                var name = Invoke(GetNameMethod, library, s_One) as string;
                into.Add(new Swatch { Color = color is Color c ? c : Color.clear, Name = name ?? "" });
            }
            s_One[0] = null;
            return true;
        }

        /// <summary>Project libraries under version control must be checked out first (PresetLibraryEditor.ListArea).</summary>
        internal static bool CurrentLibraryEditable
        {
            get
            {
                string path = CurrentLibraryPath;
                if (!IsInProject(path)) return true;
                return AssetDatabase.IsOpenForEdit(path + "." + Extension);
            }
        }

        // ---- Editing (each edit saves the file, like PresetLibraryEditor) ----------------------------

        /// <summary>Appends a swatch, like Unity's "+" button (which passes the picker's exposure-adjusted color and "").</summary>
        /// <returns>The new swatch's index, or -1.</returns>
        internal static int Add(Color color, string name = "")
        {
            if (!Available) return -1;
            var library = Resolve(true, out var path);
            if (library == null) return -1;
            s_Two[0] = color; s_Two[1] = name ?? "";
            bool added = TryInvoke(AddMethod, library, s_Two);
            s_Two[0] = null; s_Two[1] = null;
            if (!added || !Save(library, path)) return -1;
            return Count(library) - 1;
        }

        internal static bool Remove(int index)
        {
            if (!TryEdit(index, out var library, out var path)) return false;
            s_One[0] = index;
            bool ok = TryInvoke(RemoveMethod, library, s_One);
            s_One[0] = null;
            return ok && Save(library, path);
        }

        /// <summary>The context menu's "Replace": sets the swatch's color, keeps its name.</summary>
        internal static bool Replace(int index, Color color)
        {
            if (ReplaceMethod == null || !TryEdit(index, out var library, out var path)) return false;
            s_Two[0] = index; s_Two[1] = color;
            bool ok = TryInvoke(ReplaceMethod, library, s_Two);
            s_Two[0] = null; s_Two[1] = null;
            return ok && Save(library, path);
        }

        /// <summary>The context menu's "Move To First" is Move(index, 0, false). Drag reordering passes insertAfter by drop side.</summary>
        internal static bool Move(int index, int destination, bool insertAfter)
        {
            if (MoveMethod == null || !TryEdit(index, out var library, out var path)) return false;
            if (destination < 0 || destination >= Count(library)) return false;
            s_Three[0] = index; s_Three[1] = destination; s_Three[2] = insertAfter;
            bool ok = TryInvoke(MoveMethod, library, s_Three);
            s_Three[0] = null; s_Three[1] = null; s_Three[2] = null;
            return ok && Save(library, path);
        }

        static bool TryEdit(int index, out ScriptableObject library, out string path)
        {
            library = null; path = "";
            if (!Available) return false;
            library = Resolve(false, out path);
            return library != null && index >= 0 && index < Count(library);
        }

        // ---- Libraries -------------------------------------------------------------------------------

        /// <summary>
        /// The libraries Unity's settings menu lists: Preferences/Presets/*.colors, then Assets/**/Editor/*.colors, each sorted.
        /// Scans the whole Assets tree, so call it when a menu opens, not per frame.
        /// </summary>
        internal static bool ListLibraries(List<LibraryInfo> into)
        {
            into.Clear();
            var manager = Manager;
            if (!LibrariesAvailable || manager == null) return false;
            s_Three[0] = Helper; s_Three[1] = null; s_Three[2] = null;
            bool ok = TryInvoke(AvailableMethod, manager, s_Three);
            var preferences = s_Three[1] as List<string>;
            var project = s_Three[2] as List<string>;
            s_Three[0] = null; s_Three[1] = null; s_Three[2] = null;
            if (!ok) return false;
            AddLibraries(into, preferences, false);
            AddLibraries(into, project, true);
            return true;
        }

        static void AddLibraries(List<LibraryInfo> into, List<string> paths, bool inProject)
        {
            if (paths == null) return;
            paths.Sort();
            for (int i = 0; i < paths.Count; i++)
                into.Add(new LibraryInfo { Path = paths[i], Name = Path.GetFileNameWithoutExtension(paths[i]), InProject = inProject });
        }

        /// <summary>
        /// Makes <paramref name="path"/> (with or without extension) the current library for both pickers: Unity's open picker
        /// through ColorPicker.currentPresetLibrary (so it does not write its old choice back when it closes), and EditorPrefs.
        /// </summary>
        internal static bool SetCurrentLibrary(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = Path.ChangeExtension(path, null);   // PresetLibraryEditor.currentLibraryWithoutExtension
            var picker = OpenPicker;
            if (picker != null && PickerLibraryProperty != null && PickerLibraryProperty.CanWrite)
            {
                try
                {
                    PickerLibraryProperty.SetValue(picker, path);
                    (picker as EditorWindow)?.Repaint();
                }
                catch (TargetInvocationException) { }
                catch (ArgumentException) { }
            }
            EditorPrefs.SetString(PrefKey, path);
            return true;
        }

        /// <summary>
        /// Drops the loaded .colors libraries from Unity's cache so the next read comes from disk, as ColorPicker.OnDestroy does.
        /// Skipped while Unity's picker is open: it would reload mid-session.
        /// </summary>
        internal static void Unload()
        {
            var manager = Manager;
            if (manager == null || UnloadMethod == null || OpenPicker != null) return;
            s_One[0] = Helper;
            TryInvoke(UnloadMethod, manager, s_One);
            s_One[0] = null;
        }

        // ---- What a click applies ----------------------------------------------------------------------

        /// <summary>
        /// ColorPicker.OnClickedPresetSwatch: HDR pickers take the swatch as is (alpha included); LDR pickers take it as is
        /// unless a channel is above 1, then the ColorMutator base color (rescaled so the largest channel is 191/255).
        /// </summary>
        internal static Color ForPicker(Color swatch, bool hdr)
        {
            if (hdr || swatch.maxColorComponent <= 1f) return swatch;
            const byte maxByte = 191;   // ColorMutator.k_MaxByteForOverexposedColor
            float scale = maxByte / swatch.maxColorComponent;
            byte r = Math.Min(maxByte, unchecked((byte)Mathf.CeilToInt(scale * swatch.r)));
            byte g = Math.Min(maxByte, unchecked((byte)Mathf.CeilToInt(scale * swatch.g)));
            byte b = Math.Min(maxByte, unchecked((byte)Mathf.CeilToInt(scale * swatch.b)));
            byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(swatch.a) * 255f);
            return new Color32(r, g, b, a);
        }

    }
}
