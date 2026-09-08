using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace Thry.ThryEditor
{
    /// <summary>
    /// EditorWindow that plays a video file using Unity's VideoPlayer.
    /// Supports local file paths and HTTP URLs to MP4 files.
    /// </summary>
    public partial class VideoPlayerWindow : EditorWindow
    {
        private VideoPlayer _videoPlayer;
        private RenderTexture _renderTexture;
        private GameObject _videoGO;
        private string _url;
        private bool _isPrepared;
        private bool _hasError;
        private string _errorMessage;
        private double _lastClickTime;
        private bool _isDraggingSeekbar;
        private float _dragSeekTime;
        private bool _isDraggingVolume;
        private float _volume = 1f;

        // Timing for auto-hide controls
        private double _lastMouseMoveTime;

        // Cached styles
        private static GUIStyle s_timeLabel;
        private static GUIStyle s_loadingLabel;
        private static GUIStyle s_errorLabel;

        // Colors
        private static readonly Color BarBgColor = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color BarFillColor = new Color(0.85f, 0.25f, 0.25f, 1f);
        private static readonly Color ControlTextColor = new Color(0.9f, 0.9f, 0.9f, 1f);

        public static void OpenYouTube(string videoId, string title = "Video Tutorial")
        {
            if (videoId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                OpenUrl(videoId, title);
            else
                Debug.LogWarning("[Thry VideoPlayer] Cannot play YouTube ID directly. Use a direct MP4 URL.");
        }

        public static void OpenUrl(string url, string title = "Video Tutorial")
        {
            var window = GetWindow<VideoPlayerWindow>(false, title);
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(480, 300);
            window.SetVideo(url);

            var pos = window.position;
            pos.width = 860;
            pos.height = 500;
            pos.x = (Screen.currentResolution.width - pos.width) / 2;
            pos.y = (Screen.currentResolution.height - pos.height) / 2;
            window.position = pos;

            window.Show();
            window.Focus();
        }

        private void SetVideo(string url)
        {
            _url = url;
            _isPrepared = false;
            _hasError = false;
            _errorMessage = null;
            _lastMouseMoveTime = EditorApplication.timeSinceStartup;
            CleanupPlayer();
            CreatePlayer();
        }

        // Handle domain reloads (script recompilation while window is open)
        void OnEnable()
        {
            if (!string.IsNullOrEmpty(_url) && _videoPlayer == null)
            {
                SetVideo(_url);
            }
        }

        private void CreatePlayer()
        {
            _videoGO = new GameObject("ThryVideoPlayer") { hideFlags = HideFlags.HideAndDontSave };
            _videoPlayer = _videoGO.AddComponent<VideoPlayer>();

            _videoPlayer.playOnAwake = false;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _videoPlayer.isLooping = false;

            var audioSource = _videoGO.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.volume = _volume;
            audioSource.spatialBlend = 0f; // Force 2D — no 3D processing
            audioSource.bypassEffects = true;
            audioSource.bypassListenerEffects = true;
            audioSource.bypassReverbZones = true;
            _videoPlayer.SetTargetAudioSource(0, audioSource);

            _videoPlayer.source = VideoSource.Url;
            _videoPlayer.url = _url;

            _videoPlayer.prepareCompleted += OnPrepareCompleted;
            _videoPlayer.errorReceived += OnErrorReceived;
            _videoPlayer.Prepare();

            EditorApplication.update -= OnEditorUpdate; // Prevent double-subscription
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnPrepareCompleted(VideoPlayer vp)
        {
            int width = (int)vp.width;
            int height = (int)vp.height;
            if (width == 0 || height == 0) { width = 1280; height = 720; }

            if (_renderTexture != null) _renderTexture.Release();
            _renderTexture = new RenderTexture(width, height, 0) { hideFlags = HideFlags.HideAndDontSave };
            _renderTexture.Create();

            _videoPlayer.targetTexture = _renderTexture;
            _isPrepared = true;
            _videoPlayer.Play();
        }

        private void OnErrorReceived(VideoPlayer vp, string message)
        {
            _hasError = true;
            _errorMessage = message;
            Debug.LogError($"[Thry VideoPlayer] Error: {message}");
        }

        private void OnEditorUpdate()
        {
            if (_videoPlayer != null && (_videoPlayer.isPlaying || !_isPrepared))
                Repaint();
        }

        // ─────────────────────────────────────────────
        //  GUI
        // ─────────────────────────────────────────────

        void OnGUI()
        {
#if UNITY_2021_3_OR_NEWER
            if (rootVisualElement.childCount > 0) return;
#endif
            EnsureStyles();
            Rect windowRect = new Rect(0, 0, position.width, position.height);

            // Black background always
            EditorGUI.DrawRect(windowRect, Color.black);

            if (_hasError)
            {
                DrawErrorState(windowRect);
                return;
            }
            if (!_isPrepared)
            {
                DrawLoadingState(windowRect);
                return;
            }

            // Keyboard shortcuts
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Space)
                {
                    TogglePlayPause();
                    Event.current.Use();
                }
            }

            // Track mouse movement for auto-hiding controls
            if (Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseDrag)
            {
                _lastMouseMoveTime = EditorApplication.timeSinceStartup;
                Repaint();
            }

            bool showControls = ShouldShowControls(windowRect);

            // Draw video
            DrawVideoFrame(windowRect);

            // Overlay controls
            if (showControls)
            {
                DrawControlsOverlay(windowRect);
            }

            // Click to play/pause
            HandleVideoClick(windowRect);
        }

        private void DrawVideoFrame(Rect windowRect)
        {
            if (_renderTexture == null) return;

            float videoAspect = (float)_renderTexture.width / _renderTexture.height;
            float rectAspect = windowRect.width / windowRect.height;

            Rect drawRect;
            if (rectAspect > videoAspect)
            {
                float drawWidth = windowRect.height * videoAspect;
                float offset = (windowRect.width - drawWidth) / 2f;
                drawRect = new Rect(windowRect.x + offset, windowRect.y, drawWidth, windowRect.height);
            }
            else
            {
                float drawHeight = windowRect.width / videoAspect;
                float offset = (windowRect.height - drawHeight) / 2f;
                drawRect = new Rect(windowRect.x, windowRect.y + offset, windowRect.width, drawHeight);
            }

            GUI.DrawTexture(drawRect, _renderTexture, ScaleMode.StretchToFill);

            // Draw paused overlay
            if (_videoPlayer != null && !_videoPlayer.isPlaying && _isPrepared)
            {
                DrawPauseOverlay(drawRect);
            }
        }

        private void DrawPauseOverlay(Rect videoRect)
        {
            EditorGUI.DrawRect(videoRect, new Color(0, 0, 0, 0.3f));

            float iconSize = Mathf.Min(64, videoRect.width * 0.15f);
            Rect iconRect = new Rect(
                videoRect.center.x - iconSize / 2,
                videoRect.center.y - iconSize / 2,
                iconSize, iconSize);

            DrawCircle(iconRect, new Color(0, 0, 0, 0.6f));
            DrawPlayTriangle(iconRect, new Color(1, 1, 1, 0.9f));
        }

        private void DrawControlsOverlay(Rect windowRect)
        {
            float controlHeight = 42;
            float seekBarHoverHeight = 6;
            float totalHeight = controlHeight + seekBarHoverHeight;

            Rect controlArea = new Rect(0, windowRect.height - totalHeight, windowRect.width, totalHeight);

            // Gradient fade background
            Rect gradientRect = new Rect(0, controlArea.y - 40, windowRect.width, 40 + totalHeight);
            DrawGradientOverlay(gradientRect);

            // Seek bar (above control buttons)
            Rect seekRect = new Rect(0, controlArea.y, windowRect.width, seekBarHoverHeight);
            DrawSeekBar(seekRect);

            // Control bar
            Rect buttonArea = new Rect(8, controlArea.y + seekBarHoverHeight + 2, windowRect.width - 16, controlHeight - 4);
            DrawControlButtons(buttonArea);
        }

        private void DrawSeekBar(Rect rect)
        {
            if (_videoPlayer == null || _videoPlayer.length <= 0) return;

            float duration = (float)_videoPlayer.length;
            float currentTime = _isDraggingSeekbar ? _dragSeekTime : (float)_videoPlayer.time;
            float progress = Mathf.Clamp01(currentTime / duration);

            // Expand on hover
            bool isHovering = rect.Contains(Event.current.mousePosition);
            Rect expandedRect = isHovering || _isDraggingSeekbar
                ? new Rect(rect.x, rect.y - 2, rect.width, rect.height + 4)
                : rect;

            // Background track
            EditorGUI.DrawRect(expandedRect, BarBgColor);

            // Fill (progress)
            Rect fillRect = new Rect(expandedRect.x, expandedRect.y, expandedRect.width * progress, expandedRect.height);
            EditorGUI.DrawRect(fillRect, BarFillColor);

            // Handle dot (visible on hover)
            if (isHovering || _isDraggingSeekbar)
            {
                float handleSize = expandedRect.height + 8;
                Rect handleRect = new Rect(
                    expandedRect.x + expandedRect.width * progress - handleSize / 2,
                    expandedRect.center.y - handleSize / 2,
                    handleSize, handleSize);
                DrawCircle(handleRect, BarFillColor);
            }

            // Interaction — enlarged hit area for easier clicking
            Rect hitRect = new Rect(rect.x, rect.y - 8, rect.width, rect.height + 16);
            EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.Link);

            if (Event.current.type == EventType.MouseDown && hitRect.Contains(Event.current.mousePosition))
            {
                _isDraggingSeekbar = true;
                _dragSeekTime = Mathf.Clamp(((Event.current.mousePosition.x - rect.x) / rect.width) * duration, 0, duration);
                Event.current.Use();
            }
            else if (_isDraggingSeekbar && Event.current.type == EventType.MouseDrag)
            {
                _dragSeekTime = Mathf.Clamp(((Event.current.mousePosition.x - rect.x) / rect.width) * duration, 0, duration);
                Event.current.Use();
            }
            else if (_isDraggingSeekbar && Event.current.type == EventType.MouseUp)
            {
                _videoPlayer.time = _dragSeekTime;
                _isDraggingSeekbar = false;
                Event.current.Use();
            }
        }

        private void DrawControlButtons(Rect rect)
        {
            // Play/Pause
            Rect playBtn = new Rect(rect.x, rect.y, 30, rect.height);
            EditorGUIUtility.AddCursorRect(playBtn, MouseCursor.Link);
            bool isPlaying = _videoPlayer != null && _videoPlayer.isPlaying;

            if (GUI.Button(playBtn, GUIContent.none, GUIStyle.none))
                TogglePlayPause();

            if (isPlaying)
                DrawPauseIcon(playBtn, ControlTextColor);
            else
                DrawPlayTriangle(playBtn, ControlTextColor);

            // Volume icon
            Rect volumeIconRect = new Rect(playBtn.xMax + 8, rect.y, 22, rect.height);
            EditorGUIUtility.AddCursorRect(volumeIconRect, MouseCursor.Link);
            if (GUI.Button(volumeIconRect, GUIContent.none, GUIStyle.none))
            {
                _volume = _volume > 0 ? 0 : 1f;
                ApplyVolume();
            }
            DrawVolumeIcon(volumeIconRect, ControlTextColor, _volume > 0);

            // Volume slider
            Rect volumeSliderRect = new Rect(volumeIconRect.xMax + 4, rect.y + rect.height / 2 - 2, 60, 4);
            EditorGUI.DrawRect(volumeSliderRect, BarBgColor);
            Rect volumeFill = new Rect(volumeSliderRect.x, volumeSliderRect.y, volumeSliderRect.width * _volume, volumeSliderRect.height);
            EditorGUI.DrawRect(volumeFill, ControlTextColor);

            Rect volumeHitRect = new Rect(volumeSliderRect.x, volumeSliderRect.y - 10, volumeSliderRect.width, volumeSliderRect.height + 20);
            EditorGUIUtility.AddCursorRect(volumeHitRect, MouseCursor.Link);

            if (Event.current.type == EventType.MouseDown && volumeHitRect.Contains(Event.current.mousePosition))
            {
                _isDraggingVolume = true;
                _volume = Mathf.Clamp01((Event.current.mousePosition.x - volumeSliderRect.x) / volumeSliderRect.width);
                ApplyVolume();
                Event.current.Use();
            }
            else if (_isDraggingVolume && Event.current.type == EventType.MouseDrag)
            {
                _volume = Mathf.Clamp01((Event.current.mousePosition.x - volumeSliderRect.x) / volumeSliderRect.width);
                ApplyVolume();
                Event.current.Use();
            }
            else if (_isDraggingVolume && Event.current.type == EventType.MouseUp)
            {
                _isDraggingVolume = false;
                Event.current.Use();
            }

            // Time display (right-aligned)
            if (_videoPlayer != null && _videoPlayer.length > 0)
            {
                float currentTime = _isDraggingSeekbar ? _dragSeekTime : (float)_videoPlayer.time;
                float duration = (float)_videoPlayer.length;
                string timeText = $"{FormatTime(currentTime)} / {FormatTime(duration)}";

                Rect timeRect = new Rect(rect.xMax - 100, rect.y, 100, rect.height);
                GUI.Label(timeRect, timeText, s_timeLabel);
            }
        }

        private void HandleVideoClick(Rect windowRect)
        {
            // Don't interfere with control bar area
            float controlZoneY = windowRect.height - 60;

            if (Event.current.type == EventType.MouseDown && Event.current.mousePosition.y < controlZoneY)
            {
                TogglePlayPause();
                Event.current.Use();
            }
        }

        private bool ShouldShowControls(Rect windowRect)
        {
            if (_videoPlayer == null || !_videoPlayer.isPlaying) return true;
            if (_isDraggingSeekbar || _isDraggingVolume) return true;

            double elapsed = EditorApplication.timeSinceStartup - _lastMouseMoveTime;
            if (elapsed < 2.5) return true;

            if (Event.current.mousePosition.y > windowRect.height - 60) return true;

            return false;
        }

        // ─────────────────────────────────────────────
        //  Drawing primitives
        // ─────────────────────────────────────────────

        private void DrawGradientOverlay(Rect rect)
        {
            int steps = 8;
            float stepHeight = rect.height / steps;
            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / (steps - 1);
                float alpha = t * t * 0.8f;
                Rect stepRect = new Rect(rect.x, rect.y + stepHeight * i, rect.width, stepHeight + 1);
                EditorGUI.DrawRect(stepRect, new Color(0, 0, 0, alpha));
            }
        }

        private static void DrawCircle(Rect rect, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            var tex = GetWhiteCircleTexture();
            if (tex != null)
                GUI.DrawTexture(rect, tex);
            else
                EditorGUI.DrawRect(rect, color);
            GUI.color = prev;
        }

        private static Texture2D s_circleTexture;
        private static Texture2D GetWhiteCircleTexture()
        {
            if (s_circleTexture == null)
            {
                int size = 32;
                s_circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                float center = size / 2f;
                float radius = size / 2f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                        float alpha = Mathf.Clamp01((radius - dist) * 2f);
                        s_circleTexture.SetPixel(x, y, new Color(1, 1, 1, alpha));
                    }
                s_circleTexture.Apply();
            }
            return s_circleTexture;
        }

        private static void DrawPlayTriangle(Rect rect, Color color)
        {
            float padding = rect.width * 0.3f;
            float left = rect.x + padding;
            float right = rect.xMax - padding * 0.7f;
            float top = rect.y + padding;
            float bottom = rect.yMax - padding;

            int steps = 8;
            float rowHeight = (bottom - top) / steps + 0.5f;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float tFromCenter = Mathf.Abs(t - 0.5f) * 2f;
                float sliceWidth = (right - left) * (1f - tFromCenter);
                float y = Mathf.Lerp(top, bottom, t);

                Rect lineRect = new Rect(left, y, sliceWidth, rowHeight);
                EditorGUI.DrawRect(lineRect, color);
            }
        }

        private static void DrawPauseIcon(Rect rect, Color color)
        {
            float barWidth = rect.width * 0.12f;
            float gap = rect.width * 0.08f;
            float top = rect.y + rect.height * 0.25f;
            float height = rect.height * 0.5f;
            float centerX = rect.center.x;

            Rect leftBar = new Rect(centerX - gap - barWidth, top, barWidth, height);
            Rect rightBar = new Rect(centerX + gap, top, barWidth, height);
            EditorGUI.DrawRect(leftBar, color);
            EditorGUI.DrawRect(rightBar, color);
        }

        private static void DrawVolumeIcon(Rect rect, Color color, bool hasVolume)
        {
            float cx = rect.center.x;
            float cy = rect.center.y;
            float s = Mathf.Min(rect.width, rect.height) * 0.25f;

            Rect body = new Rect(cx - s * 1.2f, cy - s * 0.5f, s * 0.8f, s);
            EditorGUI.DrawRect(body, color);

            Rect cone = new Rect(body.xMax, cy - s * 0.8f, s * 0.5f, s * 1.6f);
            EditorGUI.DrawRect(cone, color);

            if (!hasVolume)
            {
                Rect x1 = new Rect(cone.xMax + 2, cy - 1, s * 1.2f, 2);
                EditorGUI.DrawRect(x1, color);
            }
        }

        // ─────────────────────────────────────────────
        //  Loading / Error states
        // ─────────────────────────────────────────────

        private void DrawLoadingState(Rect windowRect)
        {
            int dotCount = (int)(EditorApplication.timeSinceStartup * 2) % 4;
            string dots = new string('.', dotCount);
            GUI.Label(windowRect, $"Loading{dots}", s_loadingLabel);
        }

        private void DrawErrorState(Rect windowRect)
        {
            string msg = $"Could not play video\n\n{_errorMessage}";
            GUI.Label(windowRect, msg, s_errorLabel);
        }

        // ─────────────────────────────────────────────
        //  Utilities
        // ─────────────────────────────────────────────

        private void TogglePlayPause()
        {
            if (_videoPlayer == null) return;
            if (_videoPlayer.isPlaying) _videoPlayer.Pause();
            else _videoPlayer.Play();
        }

        private void ApplyVolume()
        {
            if (_videoGO == null) return;
            var audioSource = _videoGO.GetComponent<AudioSource>();
            if (audioSource != null) audioSource.volume = _volume;
        }

        private static string FormatTime(float seconds)
        {
            int totalSeconds = (int)seconds;
            int hrs = totalSeconds / 3600;
            int mins = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;
            if (hrs > 0)
                return $"{hrs}:{mins:D2}:{secs:D2}";
            return $"{mins}:{secs:D2}";
        }

        private static void EnsureStyles()
        {
            if (s_timeLabel == null)
            {
                s_timeLabel = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleRight,
                    fontSize = 11,
                    normal = { textColor = ControlTextColor }
                };
            }
            if (s_loadingLabel == null)
            {
                s_loadingLabel = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 16,
                    normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 1f) }
                };
            }
            if (s_errorLabel == null)
            {
                s_errorLabel = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                    wordWrap = true,
                    normal = { textColor = new Color(1f, 0.5f, 0.5f, 1f) }
                };
            }
        }

        // ─────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────

        void OnDestroy()
        {
            EditorApplication.update -= OnEditorUpdate;
            CleanupPlayer();
        }

        private void CleanupPlayer()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.Stop();
                _videoPlayer.prepareCompleted -= OnPrepareCompleted;
                _videoPlayer.errorReceived -= OnErrorReceived;
            }
            if (_videoGO != null) DestroyImmediate(_videoGO);
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                DestroyImmediate(_renderTexture);
            }
            _videoGO = null;
            _videoPlayer = null;
            _renderTexture = null;
            _isPrepared = false;
        }
    }
}
