#if UNITY_2021_3_OR_NEWER
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    public partial class VideoPlayerWindow
    {
        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
            var video = new Image { scaleMode = ScaleMode.ScaleToFit }; video.style.flexGrow = 1; video.style.backgroundColor = Color.black; root.Add(video);
            var status = new Label(); status.style.whiteSpace = WhiteSpace.Normal; root.Add(status);
            var seek = new Slider(0, 1); root.Add(seek);
            seek.RegisterValueChangedCallback(e => { if (_isPrepared && _videoPlayer != null && _videoPlayer.canSetTime) _videoPlayer.time = e.newValue; });
            var controls = new VisualElement(); controls.AddToClassList("thry-components"); root.Add(controls);
            var play = new Button(TogglePlayPause) { text = "Play" }; controls.Add(play);
            var time = new Label(); time.style.flexGrow = 1; time.style.alignSelf = Align.Center; controls.Add(time);
            var volume = new Slider("Volume", 0, 1) { value = _volume }; volume.style.width = 180; controls.Add(volume);
            volume.RegisterValueChangedCallback(e => { _volume = e.newValue; ApplyVolume(); });
            controls.Add(new Button(() => maximized = !maximized) { text = "Maximize" });
            video.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0) TogglePlayPause(); });
            root.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Space) { TogglePlayPause(); e.StopPropagation(); } });
            video.schedule.Execute(() =>
            {
                video.image = _renderTexture;
                status.text = _hasError ? _errorMessage : !_isPrepared ? "Loading video…" : "";
                status.style.display = string.IsNullOrEmpty(status.text) ? DisplayStyle.None : DisplayStyle.Flex;
                controls.SetEnabled(_isPrepared && !_hasError); seek.SetEnabled(_isPrepared && _videoPlayer != null && _videoPlayer.canSetTime);
                if (_videoPlayer == null || !_isPrepared) return;
                play.text = _videoPlayer.isPlaying ? "Pause" : "Play";
                seek.highValue = (float)_videoPlayer.length; seek.SetValueWithoutNotify((float)_videoPlayer.time);
                time.text = FormatTime((float)_videoPlayer.time) + " / " + FormatTime((float)_videoPlayer.length);
            }).Every(100);
        }
    }
}
#endif
