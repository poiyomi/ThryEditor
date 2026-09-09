#if UNITY_2021_3_OR_NEWER && (VRC_SDK_VRCSDK2 || VRC_SDK_VRCSDK3)
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
#if VRC_SDK_VRCSDK3
using VRC.SDKBase;
#else
using VRCSDK2;
#endif
#if VRC_SDK_VRCSDK3 && !UDON
using VRC.SDK3.Avatars.Components;
#endif

namespace Thry.ThryEditor.TexturePacker
{
    // Persist pending previews before the optimizer's order-100 upload callbacks.
    public sealed class MergePackedTexturesOnAvatarUpload : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => 99;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (Application.isPlaying) return true;
            return PackedTextureUploadCallbacks.Run(() =>
            {
                if (avatarGameObject == null) throw new InvalidOperationException("The avatar upload root is unavailable.");
                var controllers = new List<RuntimeAnimatorController>();
#if VRC_SDK_VRCSDK3 && !UDON
                var descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
                if (descriptor != null && descriptor.customizeAnimationLayers)
                {
                    foreach (var layer in descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers))
                        if (!layer.isDefault && layer.animatorController != null) controllers.Add(layer.animatorController);
                }
#endif
                return PackedTextureBuildPreparation.CollectMaterials(new[] { avatarGameObject }, controllers);
            });
        }
    }

    public sealed class MergePackedTexturesOnWorldUpload : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 99;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene || Application.isPlaying) return true;
            return PackedTextureUploadCallbacks.Run(PackedTextureUploadCallbacks.CollectWorldMaterials);
        }
    }

    internal static class PackedTextureUploadCallbacks
    {
        internal static Material[] CollectWorldMaterials()
        {
            // The SDK exports the descriptor's scene, which need not be the active scene.
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            var descriptors = Resources.FindObjectsOfTypeAll<VRC_SceneDescriptor>().Where(descriptor => descriptor != null
                && !EditorUtility.IsPersistent(descriptor) && descriptor.gameObject.scene.IsValid()
                && descriptor.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewScene(descriptor.gameObject.scene)
                && (prefabStage == null || descriptor.gameObject.scene != prefabStage.scene)).ToArray();
            if (descriptors.Length != 1)
                throw new InvalidOperationException("Texture preparation requires exactly one world descriptor in the loaded scenes.");
            return CollectWorldMaterials(descriptors[0].gameObject.scene);
        }

        internal static Material[] CollectWorldMaterials(Scene scene)
        {
            var materials = PackedTextureBuildPreparation.CollectMaterials(scene.GetRootGameObjects()).ToList();
            var skybox = SceneSkybox(scene);
            if (skybox != null) materials.Add(skybox);
            return materials.Distinct().ToArray();
        }

        internal static Material SceneSkybox(Scene scene)
        {
            var previous = SceneManager.GetActiveScene();
            if (previous == scene) return RenderSettings.skybox;
            try
            {
                if (!SceneManager.SetActiveScene(scene)) throw new InvalidOperationException("Could not read the world scene's skybox settings.");
                return RenderSettings.skybox;
            }
            finally
            {
                if (SceneManager.GetActiveScene() != previous && previous.IsValid() && previous.isLoaded && !SceneManager.SetActiveScene(previous))
                    throw new InvalidOperationException("Could not restore the active scene after reading the world skybox.");
            }
        }

        internal static bool Run(Func<IEnumerable<Material>> collect)
        {
            try
            {
                int count = PackedTextureBuildPreparation.Prepare(collect());
                if (count > 0) Debug.Log("[Thry] Merged " + count + " pending packed texture(s) before upload.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Thry] Texture preparation stopped the upload: " + exception.Message);
                return false;
            }
        }
    }
}
#endif
