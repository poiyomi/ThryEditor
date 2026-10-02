#if VRC_SDK_VRCSDK3
using System;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Thry.ThryEditor.UploadCallbacks // sry Pumkin for taking away your namespace. Just tring to tidy up a bit
{
    public class VRCAutoAnchor : IVRCSDKPreprocessAvatarCallback
    {
        internal static GameObject OptedOutAvatar;

        public int callbackOrder => 0;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            try
            {
                bool skip = avatarGameObject == OptedOutAvatar || UploadAnchorOverrideSetter.ShouldSkipAvatar(avatarGameObject);
                OptedOutAvatar = null;
                if(!skip)
                    UploadAnchorOverrideSetter.SetAnchorOverrides(avatarGameObject);
            }
            catch(Exception ex)
            {
                Debug.LogException(ex);
            }
            return true;
        }
    }

    // Optimizers and the SDK's EditorOnly stripping can delete the empty opt-out object before order 0
    public class VRCAutoAnchorOptOut : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            VRCAutoAnchor.OptedOutAvatar = UploadAnchorOverrideSetter.ShouldSkipAvatar(avatarGameObject) ? avatarGameObject : null;
            return true;
        }
    }
}
#endif
