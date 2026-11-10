// Material/Shader Inspector for Unity 2021/2022/6
// Copyright (C) 2019-2026 Thryrallo

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Finds material properties that an avatar's animation clips change but locking bakes,
    /// because they are not marked Animated or Renamed.
    /// </summary>
    public static class AnimatedPropertyCheck
    {
        public sealed class Item
        {
            public Material Material;
            public string Property;
            /// <summary>The clips use the renamed property name, so the mark it needs is Renamed.</summary>
            public bool Renamed;
            /// <summary>Already baked. The material has to be unlocked before a mark can apply.</summary>
            public bool Locked;
            public string RendererPath;
            public readonly List<string> Clips = new List<string>();

            public string ClipList(int max)
            {
                if (Clips.Count <= max) return string.Join(", ", Clips);
                return string.Join(", ", Clips.Take(max)) + " +" + (Clips.Count - max);
            }
        }

        public const string UndoName = "Mark animated properties";

        const string MaterialPrefix = "material.";
        const int MaxPropertySteps = 50000;
        const int MaxPropertyDepth = 32;
        const int MaxLoggedItems = 40;

        enum State { None, Live, Unmarked }

        struct Curve
        {
            public string Path;
            public Type Type;
            public string Property;
        }

        sealed class ClipCurves
        {
            public Curve[] Curves;
            public int Dirty;
            public int Imports;
        }

        struct Source
        {
            public Transform Owner;
            public UnityEngine.Object Asset;
            // An Animator plays its controller on its own transform, so those paths resolve nowhere else.
            public bool Strict;
        }

        sealed class RootSources
        {
            public MonoBehaviour[] Behaviours;
            public int[] Dirty;
            public List<Source>[] Found;
            public int Imports;
        }

        sealed class MaterialInfo
        {
            public Shader Shader;
            public bool Locked;
            public string Suffix;
        }

        static readonly ConditionalWeakTable<AnimationClip, ClipCurves> s_clipCurves = new ConditionalWeakTable<AnimationClip, ClipCurves>();
        static readonly ConditionalWeakTable<GameObject, RootSources> s_sources = new ConditionalWeakTable<GameObject, RootSources>();
        static readonly List<Animator> s_animators = new List<Animator>();
        static readonly List<MonoBehaviour> s_behaviours = new List<MonoBehaviour>();
        static int s_imports;

        /// <summary>
        /// Checks the clips of every controller the avatar references, plus <paramref name="clips"/>, which
        /// are read as playing on the avatar root.
        /// </summary>
        public static List<Item> Find(GameObject avatarRoot, IEnumerable<AnimationClip> clips = null)
        {
            List<Item> items = new List<Item>();
            if (avatarRoot == null) return items;
            Transform root = avatarRoot.transform;

            Dictionary<(Renderer, string), List<AnimationClip>> animated = new Dictionary<(Renderer, string), List<AnimationClip>>();
            Dictionary<(Transform, string), Renderer> targets = new Dictionary<(Transform, string), Renderer>();
            HashSet<(Transform, AnimationClip)> added = new HashSet<(Transform, AnimationClip)>();

            void Add(AnimationClip clip, Transform owner, bool strict)
            {
                if (clip == null || owner == null || !added.Add((owner, clip))) return;
                foreach (Curve curve in GetCurves(clip))
                {
                    Renderer renderer = Resolve(owner, curve, targets);
                    if (renderer == null && !strict && owner != root) renderer = Resolve(root, curve, targets);
                    if (renderer == null) continue;

                    if (!animated.TryGetValue((renderer, curve.Property), out List<AnimationClip> users))
                        animated.Add((renderer, curve.Property), users = new List<AnimationClip>());
                    if (!users.Contains(clip)) users.Add(clip);
                }
            }

            List<Source> sources = new List<Source>();
            GetSources(avatarRoot, sources);
            foreach (Source source in sources)
            {
                if (source.Asset is AnimationClip single) Add(single, source.Owner, source.Strict);
                else if (source.Asset is RuntimeAnimatorController controller)
                    foreach (AnimationClip clip in controller.animationClips) Add(clip, source.Owner, source.Strict);
            }
            if (clips != null)
                foreach (AnimationClip clip in clips) Add(clip, root, true);

            Dictionary<Material, MaterialInfo> infos = new Dictionary<Material, MaterialInfo>();
            Dictionary<Renderer, Material[]> slots = new Dictionary<Renderer, Material[]>();
            Dictionary<(Material, string), Item> found = new Dictionary<(Material, string), Item>();
            List<Item> candidates = new List<Item>();

            foreach (KeyValuePair<(Renderer, string), List<AnimationClip>> pair in animated)
            {
                Renderer renderer = pair.Key.Item1;
                if (!slots.TryGetValue(renderer, out Material[] materials)) slots.Add(renderer, materials = renderer.sharedMaterials);

                // A material curve drives every material on the renderer. When one of them already takes the
                // animation, the others were most likely left baked on purpose.
                candidates.Clear();
                bool live = false;
                foreach (Material material in materials)
                {
                    State state = Classify(material, pair.Key.Item2, infos, out string property, out bool renamed, out bool locked);
                    if (state == State.Live) { live = true; break; }
                    if (state == State.Unmarked)
                        candidates.Add(new Item { Material = material, Property = property, Renamed = renamed, Locked = locked });
                }
                if (live) continue;

                foreach (Item candidate in candidates)
                {
                    if (!found.TryGetValue((candidate.Material, candidate.Property), out Item item))
                    {
                        item = candidate;
                        string path = AnimationUtility.CalculateTransformPath(renderer.transform, root);
                        item.RendererPath = path.Length == 0 ? root.name : root.name + "/" + path;
                        found.Add((item.Material, item.Property), item);
                        items.Add(item);
                    }
                    foreach (AnimationClip clip in pair.Value)
                        if (!item.Clips.Contains(clip.name)) item.Clips.Add(clip.name);
                }
            }

            items.Sort((a, b) =>
            {
                int order = string.CompareOrdinal(a.Material.name, b.Material.name);
                return order != 0 ? order : string.CompareOrdinal(a.Property, b.Property);
            });
            return items;
        }

        /// <summary>Marks the items Animated, or Renamed where the clips need that, as one undo step.</summary>
        public static void Mark(IEnumerable<Item> items)
        {
            List<Item> marked = items.Where(i => i.Material != null && !i.Locked && i.Material.HasProperty(i.Property)).ToList();
            if (marked.Count == 0) return;

            Undo.RecordObjects(marked.Select(i => (UnityEngine.Object)i.Material).Distinct().ToArray(), UndoName);
            foreach (Item item in marked)
            {
                ShaderOptimizer.SetAnimatedTag(MaterialEditor.GetMaterialProperty(new UnityEngine.Object[] { item.Material }, item.Property), item.Renamed ? "2" : "1");
                EditorUtility.SetDirty(item.Material);
                ShaderProperty.InvalidateRetainedAnimatedOwner(item.Material);
            }
        }

        /// <summary>For the upload hook: one warning that names what <see cref="Find"/> reports, or nothing.</summary>
        public static void LogUnmarked(GameObject avatarRoot)
        {
            List<Item> items;
            try
            {
                items = Find(avatarRoot);
            }
            catch (Exception e)
            {
                // The upload must not fail over a check.
                Debug.LogException(e);
                return;
            }
            if (items.Count == 0) return;

            StringBuilder message = new StringBuilder("These properties are animated but not marked Animated, so locking bakes them and the animations will do nothing after upload. "
                + "Right-click each one and choose Animated (when locked), or use Poi > Thry > Material Lock Manager.");
            for (int i = 0; i < items.Count && i < MaxLoggedItems; i++)
            {
                Item item = items[i];
                message.Append('\n').Append(item.Material.name);
                if (item.Locked) message.Append(" (locked)");
                message.Append(": ").Append(item.Property).Append(" (").Append(item.ClipList(3)).Append(')');
            }
            if (items.Count > MaxLoggedItems) message.Append("\n+").Append(items.Count - MaxLoggedItems).Append(" more");
            ThryLogger.LogWarn("Locking", message.ToString());
        }

        static Renderer Resolve(Transform from, Curve curve, Dictionary<(Transform, string), Renderer> targets)
        {
            if (!targets.TryGetValue((from, curve.Path), out Renderer renderer))
            {
                Transform target = curve.Path.Length == 0 ? from : from.Find(curve.Path);
                renderer = target != null ? target.GetComponent<Renderer>() : null;
                targets.Add((from, curve.Path), renderer);
            }
            return renderer != null && curve.Type.IsInstanceOfType(renderer) ? renderer : null;
        }

        static Curve[] GetCurves(AnimationClip clip)
        {
            ClipCurves cached = s_clipCurves.GetValue(clip, _ => new ClipCurves());
            int dirty = EditorUtility.GetDirtyCount(clip);
            if (cached.Curves != null && cached.Dirty == dirty && cached.Imports == s_imports) return cached.Curves;

            List<Curve> curves = null;
            HashSet<(string, Type, string)> seen = null;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type == null || !typeof(Renderer).IsAssignableFrom(binding.type)) continue;
                if (!binding.propertyName.StartsWith(MaterialPrefix, StringComparison.Ordinal)) continue;

                // material._Color.r, material._MainTex_ST.x
                string property = binding.propertyName.Substring(MaterialPrefix.Length);
                int component = property.IndexOf('.');
                if (component >= 0) property = property.Substring(0, component);
                if (property.Length == 0) continue;

                if (curves == null)
                {
                    curves = new List<Curve>();
                    seen = new HashSet<(string, Type, string)>();
                }
                if (seen.Add((binding.path, binding.type, property)))
                    curves.Add(new Curve { Path = binding.path, Type = binding.type, Property = property });
            }

            cached.Curves = curves != null ? curves.ToArray() : Array.Empty<Curve>();
            cached.Dirty = dirty;
            cached.Imports = s_imports;
            return cached.Curves;
        }

        static void GetSources(GameObject avatarRoot, List<Source> sources)
        {
            avatarRoot.GetComponentsInChildren(true, s_animators);
            foreach (Animator animator in s_animators)
            {
                if (animator.runtimeAnimatorController != null)
                    sources.Add(new Source { Owner = animator.transform, Asset = animator.runtimeAnimatorController, Strict = true });
            }
            s_animators.Clear();

            // Reading a component's references is the slow part, so each one is read again only after it changed.
            avatarRoot.GetComponentsInChildren(true, s_behaviours);
            RootSources cached = s_sources.GetValue(avatarRoot, _ => new RootSources());
            bool same = cached.Behaviours != null && cached.Imports == s_imports && cached.Behaviours.Length == s_behaviours.Count;
            for (int i = 0; same && i < s_behaviours.Count; i++) same = ReferenceEquals(s_behaviours[i], cached.Behaviours[i]);
            if (!same)
            {
                cached.Behaviours = s_behaviours.ToArray();
                cached.Dirty = new int[s_behaviours.Count];
                cached.Found = new List<Source>[s_behaviours.Count];
                cached.Imports = s_imports;
            }
            s_behaviours.Clear();

            for (int i = 0; i < cached.Behaviours.Length; i++)
            {
                MonoBehaviour behaviour = cached.Behaviours[i];
                // Null for a missing script.
                if (behaviour == null) continue;

                int dirty = EditorUtility.GetDirtyCount(behaviour);
                if (!same || dirty != cached.Dirty[i])
                {
                    cached.Dirty[i] = dirty;
                    cached.Found[i] = CollectReferences(behaviour);
                }
                if (cached.Found[i] != null) sources.AddRange(cached.Found[i]);
            }
        }

        // Reads controllers and clips off any component, so the avatar descriptor, Modular Avatar and VRCFury
        // are covered without referencing their types.
        static List<Source> CollectReferences(MonoBehaviour behaviour)
        {
            List<Source> found = null;
            using (SerializedObject serialized = new SerializedObject(behaviour))
            {
                SerializedProperty property = serialized.GetIterator();
                bool enter = true;
                for (int steps = 0; steps < MaxPropertySteps && property.Next(enter); steps++)
                {
                    enter = false;
                    SerializedPropertyType type = property.propertyType;
                    if (type == SerializedPropertyType.ObjectReference)
                    {
                        UnityEngine.Object value = property.objectReferenceValue;
                        if (value is RuntimeAnimatorController || value is AnimationClip)
                        {
                            if (found == null) found = new List<Source>();
                            found.Add(new Source { Owner = behaviour.transform, Asset = value });
                        }
                    }
                    else if (type == SerializedPropertyType.Generic || type == SerializedPropertyType.ManagedReference)
                    {
                        enter = property.depth < MaxPropertyDepth && (!property.isArray || HoldsReferences(property));
                    }
                }
            }
            return found;
        }

        // Skips arrays of plain values, which can be long. An array iterates as itself, Array, size, data[0].
        static bool HoldsReferences(SerializedProperty array)
        {
            SerializedProperty element = array.Copy();
            for (int i = 0; i < 3 && element.Next(true); i++)
            {
                if (!element.propertyPath.EndsWith("]", StringComparison.Ordinal)) continue;
                SerializedPropertyType type = element.propertyType;
                return type == SerializedPropertyType.ObjectReference || type == SerializedPropertyType.Generic || type == SerializedPropertyType.ManagedReference;
            }
            return false;
        }

        static MaterialInfo GetInfo(Material material)
        {
            Shader shader = material.shader;
            // A material that is not an asset is never locked.
            if (shader.IsBroken() || !AssetDatabase.Contains(material)) return null;

            bool locked = ShaderOptimizer.IsShaderLocked(shader);
            if (!locked)
            {
                shader = SectionLock.GetSourceShader(shader);
                if (shader == null || !ShaderOptimizer.IsShaderUsingThryOptimizer(shader)) return null;
            }

            string suffix = locked ? material.GetTag(ShaderOptimizer.TAG_LOCKED_RENAME_SUFFIX, false, "") : "";
            if (suffix.Length == 0) suffix = ShaderOptimizer.GetRenamedPropertySuffix(material);
            return new MaterialInfo { Shader = shader, Locked = locked, Suffix = suffix.Length == 0 ? null : "_" + suffix };
        }

        static State Classify(Material material, string name, Dictionary<Material, MaterialInfo> infos, out string property, out bool renamed, out bool locked)
        {
            property = null;
            renamed = false;
            locked = false;
            if (material == null) return State.None;
            if (!infos.TryGetValue(material, out MaterialInfo info)) infos.Add(material, info = GetInfo(material));
            if (info == null) return State.None;

            locked = info.Locked;
            Shader shader = info.Shader;
            string suffix = info.Suffix;

            int index = FindProperty(material, shader, name, out property);
            if (index < 0)
            {
                // The clip may use the name a Renamed property gets when locked.
                index = suffix != null ? FindRenamed(material, shader, name, suffix, out property) : -1;
                if (index < 0 || !ShaderOptimizer.CanRename(property)) return State.None;
                renamed = true;
            }
            else if (locked && suffix != null && property.EndsWith(suffix, StringComparison.Ordinal)
                && ShaderOptimizer.GetAnimatedTag(material, property.Substring(0, property.Length - suffix.Length)) == "2")
            {
                // The locked shader's renamed copy.
                return State.Live;
            }

            string tag = ShaderOptimizer.GetAnimatedTag(material, property);
            if (tag != "" && tag != "0")
                return (tag == "2" && ShaderOptimizer.CanRename(property)) == renamed ? State.Live : State.None;

            if (!RetainedShaderDeclarations.Get(shader, index).IsAnimatable) return State.None;
            return IsExemptFromLocking(shader, index, property) ? State.Live : State.Unmarked;
        }

        static int FindProperty(Material material, Shader shader, string name, out string property)
        {
            property = name;
            if (material.HasProperty(name)) return shader.FindPropertyIndex(name);
            if (!name.EndsWith("_ST", StringComparison.Ordinal)) return -1;

            property = name.Substring(0, name.Length - 3);
            return FindTexture(material, shader, property);
        }

        static int FindRenamed(Material material, Shader shader, string name, string suffix, out string property)
        {
            property = null;
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                property = name.Substring(0, name.Length - suffix.Length);
                if (material.HasProperty(property)) return shader.FindPropertyIndex(property);
            }
            if (!name.EndsWith(suffix + "_ST", StringComparison.Ordinal)) return -1;

            property = name.Substring(0, name.Length - suffix.Length - 3);
            return FindTexture(material, shader, property);
        }

        static int FindTexture(Material material, Shader shader, string property)
        {
            if (!material.HasProperty(property)) return -1;
            int index = shader.FindPropertyIndex(property);
            return index >= 0 && shader.GetPropertyType(index) == ShaderPropertyType.Texture ? index : -1;
        }

        // The same properties ShaderOptimizer.Lock never turns into constants.
        static bool IsExemptFromLocking(Shader shader, int index, string property)
        {
            return property.EndsWith(ShaderOptimizer.AnimatedPropertySuffix, StringComparison.Ordinal)
                || property == ShaderOptimizer.UseInlineSamplerStatesPropertyName
                || shader.GetPropertyDescription(index).EndsWith(ShaderOptimizer.ExemptFromLockingSuffix, StringComparison.Ordinal)
                || (shader.GetPropertyType(index) != ShaderPropertyType.Texture && shader.GetPropertyFlags(index).HasFlag(ShaderPropertyFlags.NonModifiableTextureData))
                || shader.GetPropertyAttributes(index).Contains("DoNotLock");
        }

        // A reimport can replace a clip's curves or a prefab instance's references without changing a dirty
        // count. Materials and shaders, which are all that locking imports, can do neither.
        class ImportWatcher : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                foreach (string path in imported)
                {
                    if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)) continue;
                    s_imports++;
                    return;
                }
            }
        }
    }
}
