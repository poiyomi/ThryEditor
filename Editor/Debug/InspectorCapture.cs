// InspectorCapture.cs from https://gist.github.com/markeahogan/69fc4d9722eadc20882c9aeda261fc56

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Provides functionality for screenshotting full editor windows, and the inspector in particular
    /// </summary>
    public static class InspectorCapture
    {
        private const int _tabsHeight = 20;
        private const int _footer = 4;

        /// <summary>
        /// Takes a screenshot of the entire inspector
        /// <param name="saveDirectory">Directory to save screenshot to</param>
        /// </summary>
        public static async void CaptureActiveInspector(string saveDirectory)
        {
            var inspector = EditorWindow.focusedWindow;
            if(inspector == null)
                return;
            await Task.Delay(250);
            await CaptureWindow(inspector, saveDirectory);
        }

        /// <summary>
        /// Captures the window and saves it as a png in the project folder
        /// </summary>
        /// <param name="window">window to screenshot</param>
        /// <param name="saveDirectory">Directory to save screenshot to</param>
        /// <returns>Task completed when the process finishes</returns>
        public static async Task CaptureWindow(EditorWindow window, string saveDirectory)
        {
            var bytes = (await ScreenshotAsync(window)).EncodeToPNG();
            var filename = $"{window.GetType().Name}_{DateTime.Now.ToString("HH-mm-ss")}.png";
            var finalPath = $"{saveDirectory}/{filename}";
            if(Directory.Exists(saveDirectory))
            {
                await File.WriteAllBytesAsync(finalPath, bytes);
                Debug.Log($"Saved screenshot to {finalPath}");
            }
            else
            {
                Debug.LogError($"Can't save screenshot {finalPath} because directory doesn't exist.");
            }
        }

        /// <summary>
        /// Scrolls the window to the top then incrementally scrolls to the bottom taking screenshots till the whole thing is captured
        /// </summary>
        /// <param name="window">The window to screenshot</param>
        /// <returns>A texture containing the editor window</returns>
        static async Task<Texture2D> ScreenshotAsync(EditorWindow window)
        {
            InitReflections(window);
            
            List<Color> pixels = new List<Color>();
            bool canScroll = GetScrollView(window) != null;
            float originalScroll = SetScroll(window);

            var baseHeight = window.position.height - (_tabsHeight + _footer);

            bool originalExpanded = GetPreviewExpanded(window) ?? false;
            SetPreviewExpanded(window, false);

            int width = (int)window.position.width;
            int maxHeight = SystemInfo.maxTextureSize;
            bool truncated = false;
            for(int i = 0; ; i++)
            {
                float desiredScroll = baseHeight * i;
                float scroll = await ScrollTo(desiredScroll);
                int offset = (int)(desiredScroll - scroll);

                Color[] chunk = ReadWindowPixels(window, offset);
                if(chunk.Length == 0)
                    break;
                // Rows are bottom to top, so keep the end of the chunk
                int remainingRows = maxHeight - pixels.Count / width;
                if(chunk.Length / width > remainingRows)
                {
                    pixels.InsertRange(0, new ArraySegment<Color>(chunk, chunk.Length - remainingRows * width, remainingRows * width));
                    truncated = true;
                    break;
                }
                pixels.InsertRange(0, chunk);

                if(offset > 0 || !canScroll)
                    break;
            }

            SetScroll(window, originalScroll);
            SetPreviewExpanded(window, originalExpanded);

            if(truncated)
                Debug.LogWarning($"Inspector is taller than the maximum texture size. Only the top {maxHeight} pixels were captured.");

            int height = pixels.Count / width;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.SetPixels(pixels.ToArray());

            return texture;

            // wraps scrolling and delayedCall in a Task as scrolling requires a frame to update 
            Task<float> ScrollTo(float scroll)
            {
                float result = SetScroll(window, scroll);
                var tcs = new TaskCompletionSource<float>();
                EditorApplication.delayCall += () => tcs.TrySetResult(result);
                return tcs.Task;
            }
        }

        /// <summary>
        /// Captures a section of the window
        /// </summary>
        /// <param name="window">Window to capture</param>
        /// <param name="offset">an optional vertical offset from the bottom</param>
        /// <returns></returns>
        private static Color[] ReadWindowPixels(EditorWindow window, int offset = 0)
        {
            int width = (int)window.position.width;
            int height = (int)window.position.height - (_tabsHeight + _footer + offset);
            return UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(
                window.position.position + new Vector2(0, _tabsHeight + offset), width, height);
        }

        /// <summary>
        /// Sets the editor window's scroll, by directly setting the ScrollViewField's value. Returns the resulting scroll value
        /// </summary>
        /// <param name="inspector">The window to scroll</param>
        /// <param name="scroll">the y value of the scroll</param>
        /// <returns>the value passed in clamped to the min max scroll of the window</returns>
        static float SetScroll(EditorWindow inspector, float scroll = -1)
        {
            var scrollView = GetScrollView(inspector);
            if(scrollView == null) return 0;
            if(scroll >= 0) scrollView.scrollOffset = new Vector2(0, scroll);
            return scrollView.scrollOffset.y;
        }

        static ScrollView GetScrollView(EditorWindow window)
        {
            return ScrollViewField?.GetValue(window) as ScrollView;
        }

        static FieldInfo PreviewResizerField;
        static MethodInfo PreviewResizerGetExpandedMethod;
        static MethodInfo PreviewResizerSetExpandedMethod;
        static FieldInfo ScrollViewField;
        static Type lastWindowType;
        
        static void InitReflections(EditorWindow window)
        {
            if(window.GetType() == lastWindowType)
                return;
            
            // Cached fields belong to the previous window type
            lastWindowType = window.GetType();
            PreviewResizerField = lastWindowType.GetField("m_PreviewResizer", BindingFlags.NonPublic | BindingFlags.Instance);
            PreviewResizerGetExpandedMethod = null;
            PreviewResizerSetExpandedMethod = null;

            if(PreviewResizerField != null)
            {
                Type resizerType = PreviewResizerField.FieldType;
                PreviewResizerGetExpandedMethod = resizerType.GetMethod("GetExpanded");
                PreviewResizerSetExpandedMethod = resizerType.GetMethod("SetExpanded", new[] {typeof(bool)});
            }
            
            ScrollViewField = lastWindowType.GetField("m_ScrollView", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        static bool? GetPreviewExpanded(EditorWindow window)
        {
            if(PreviewResizerField == null || PreviewResizerGetExpandedMethod == null)
                return null;
            
            var previewResizer = PreviewResizerField.GetValue(window);
            if(previewResizer == null)
                return null;
            return (bool)PreviewResizerGetExpandedMethod.Invoke(previewResizer, null);
        }

        static void SetPreviewExpanded(EditorWindow window, bool expanded)
        {
            if(PreviewResizerSetExpandedMethod == null)
                return;
            
            var previewResizer = PreviewResizerField.GetValue(window);
            if(previewResizer == null)
                return;
            PreviewResizerSetExpandedMethod.Invoke(previewResizer, new object[] { expanded });
        }
    }
}
