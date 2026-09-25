using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed class MaskLevelsTrack : VisualElement
    {
        readonly Func<float[]> read;
        readonly Action<int,float> write;
        int selected, pointer=-1;
        readonly List<VisualElement> handles = new List<VisualElement>();
        public MaskLevelsTrack(Func<float[]> read,Action<int,float> write,Action begin)
        {
            this.read=read; this.write=write; AddToClassList("poi-levels-track"); focusable=true;
            tooltip="Drag a handle. Arrow keys adjust the selected handle; Tab moves to numeric entry.";
            AddToClassList("unity-slider"); AddToClassList("unity-base-slider--horizontal");
            var rail = new VisualElement { pickingMode = PickingMode.Ignore };
            rail.style.position = Position.Absolute; rail.style.left = 8f; rail.style.right = 8f; rail.style.top = 0f; rail.style.bottom = 0f; Add(rail);
            var tracker = new VisualElement { pickingMode = PickingMode.Ignore };
            tracker.AddToClassList("unity-base-slider__tracker"); rail.Add(tracker);
            var initialValues = read();
            for(int i=0;i<initialValues.Length;i++)
            {
                var handle = new VisualElement { pickingMode = PickingMode.Ignore };
                handle.AddToClassList("unity-base-slider__dragger");
                if(initialValues.Length == 3 && i == 1) handle.AddToClassList("poi-levels-midpoint");
                handle.style.marginLeft = -3f; rail.Add(handle); handles.Add(handle);
            }
            RefreshHandles();
            RegisterCallback<PointerDownEvent>(e => {
                if(e.button!=0)return; Focus(); float x=Value(e.localPosition.x); var values=read(); selected=0;
                for(int i=1;i<values.Length;i++) if(Mathf.Abs(values[i]-x)<Mathf.Abs(values[selected]-x) || (Mathf.Approximately(values[i],values[selected]) && x>=values[i]))selected=i;
                begin(); pointer=e.pointerId; this.CapturePointer(pointer); write(selected,x); e.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(e => { if(pointer!=e.pointerId || !this.HasPointerCapture(pointer))return; write(selected,Value(e.localPosition.x));e.StopPropagation(); });
            RegisterCallback<PointerUpEvent>(e => { if(pointer!=e.pointerId)return;this.ReleasePointer(pointer);pointer=-1;e.StopPropagation(); });
            RegisterCallback<PointerCaptureOutEvent>(e => pointer=-1);
            RegisterCallback<KeyDownEvent>(e => { if(e.keyCode!=KeyCode.LeftArrow && e.keyCode!=KeyCode.RightArrow)return;begin();write(selected,read()[selected]+(e.keyCode==KeyCode.RightArrow?1:-1)*(e.shiftKey?.1f:.01f));e.StopPropagation(); });
        }
        float Value(float x) => Mathf.Clamp01((x-8)/Mathf.Max(1,contentRect.width-16));
        public void RefreshHandles()
        {
            var values = read();
            for(int i=0;i<handles.Count;i++) handles[i].style.left = Length.Percent(values[i]*100);
        }
    }
}
