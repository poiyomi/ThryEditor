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
        private float _volume = 1f;

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
