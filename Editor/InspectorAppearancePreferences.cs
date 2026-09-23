using System;
using System.IO;
using UnityEngine;

namespace Thry.ThryEditor
{
    // Only Theme values belong here. Config keeps the project's original values so opting
    // out of sharing restores them, including after a domain reload or editor restart.
    internal sealed class InspectorAppearancePreferences
    {
        internal static InspectorAppearancePreferences Shared { get; set; } = new InspectorAppearancePreferences(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ThryEditor", "InspectorAppearance.json"));

        internal static readonly string[] Fields = {
            nameof(Config.inspectorDarkGray), nameof(Config.inspectorMediumGray), nameof(Config.inspectorLightGray),
            nameof(Config.inspectorTextSize), nameof(Config.inspectorPropertyHeight), nameof(Config.inspectorHeaderHeight)
        };
        private readonly string path;
        private string lastWarning;

        internal InspectorAppearancePreferences(string path) { this.path = path; }

        [Serializable]
        private sealed class Profile
        {
            public int version;
            public int inspectorDarkGray, inspectorMediumGray, inspectorLightGray;
            public InspectorTextSize inspectorTextSize;
            public int inspectorPropertyHeight = 18;
            public int inspectorHeaderHeight = 22;

            internal static Profile From(Config config) => new Profile {
                version = 1,
                inspectorDarkGray = config.inspectorDarkGray,
                inspectorMediumGray = config.inspectorMediumGray,
                inspectorLightGray = config.inspectorLightGray,
                inspectorTextSize = config.inspectorTextSize,
                inspectorPropertyHeight = config.inspectorPropertyHeight,
                inspectorHeaderHeight = config.inspectorHeaderHeight
            };

            internal void Apply(Config config)
            {
                config.inspectorDarkGray = Mathf.Clamp(inspectorDarkGray, -20, 20);
                config.inspectorMediumGray = Mathf.Clamp(inspectorMediumGray, -20, 20);
                config.inspectorLightGray = Mathf.Clamp(inspectorLightGray, -20, 20);
                config.inspectorTextSize = Enum.IsDefined(typeof(InspectorTextSize), inspectorTextSize)
                    ? inspectorTextSize : InspectorTextSize.Default;
                config.inspectorPropertyHeight = inspectorPropertyHeight == 20 || inspectorPropertyHeight == 22 ? inspectorPropertyHeight : 18;
                config.inspectorHeaderHeight = inspectorHeaderHeight == 18 || inspectorHeaderHeight == 20 || inspectorHeaderHeight == 24 ? inspectorHeaderHeight : 22;
            }
        }

        private Profile Load()
        {
            if (!File.Exists(path)) return null;
            Profile profile;
            // Allow another editor to atomically replace the profile while this reader is open on Windows.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                profile = JsonUtility.FromJson<Profile>(reader.ReadToEnd());
            if (profile == null || profile.version != 1)
                throw new InvalidDataException("Unsupported inspector appearance profile.");
            return profile;
        }

        internal bool HasProfile
        {
            get { return TryLoad() != null; }
        }

        private Profile TryLoad()
        {
            try { var profile = Load(); lastWarning = null; return profile; }
            catch (Exception e) when (e is InvalidDataException || e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                if (lastWarning != e.Message)
                    Debug.LogWarning("[ThryEditor] Could not read shared appearance; using this project's appearance. " + e.Message);
                lastWarning = e.Message;
                return null;
            }
        }

        internal Config Get(Config project)
        {
            if (!project.useSharedInspectorAppearance) return project;
            var profile = TryLoad();
            if (profile == null) return project;
            var appearance = new Config();
            profile.Apply(appearance);
            return appearance;
        }

        internal void SetShared(Config project, bool enabled)
        {
            if (enabled)
            {
                // A project joining an existing profile must never seed over its preferences.
                if (Load() == null) Write(Profile.From(project), onlyIfMissing: true);
            }
            project.useSharedInspectorAppearance = enabled;
        }

        internal void SetValue(Config project, string field, object value)
        {
            if (Array.IndexOf(Fields, field) < 0) throw new ArgumentException("Not an appearance setting.", nameof(field));
            var target = project;
            if (project.useSharedInspectorAppearance)
            {
                // Read immediately before changing a field so another project's other changes survive.
                var profile = Load() ?? Profile.From(project);
                target = new Config(); profile.Apply(target);
            }
            typeof(Config).GetField(field).SetValue(target, value);
            if (project.useSharedInspectorAppearance) Write(Profile.From(target));
        }

        internal void Reset(Config project)
        {
            var defaults = Profile.From(new Config());
            if (project.useSharedInspectorAppearance) Write(defaults);
            else defaults.Apply(project);
        }

        private void Write(Profile profile, bool onlyIfMissing = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(profile, true));
                // Rename in the same directory keeps readers from seeing partially written JSON.
                if (!File.Exists(path))
                {
                    try { File.Move(temporary, path); return; }
                    catch (IOException) when (File.Exists(path)) { }
                }
                if (!onlyIfMissing) File.Replace(temporary, path, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
