// Material/Shader Inspector for Unity 2017/2018
// Copyright (C) 2019 Thryrallo

using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    public abstract class ModuleSettings
    {
        public const string MODULES_CONFIG = "Thry/modules_config";

        public abstract void Draw();
    }

    public partial class Settings : EditorWindow
    {
        public static void OpenDowngradePopup()
        {
            Settings window = (Settings)EditorWindow.GetWindow(typeof(Settings));
            window.Show();
        }

        public new void Show()
        {
            base.Show();
            this.titleContent = new GUIContent("Thry Settings");
        }
    }
}