using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Unity's internal eyedropper, for UI Toolkit and IMGUI hosts. Colors are display colors.
    /// Exactly one of the picked and cancelled callbacks runs for every session.
    /// </summary>
    internal static class ThryEyeDropper
    {
        const string StartCommand = "ThryEyeDropperStart", TrampolineName = "thry-eyedropper-trampoline";
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly Type DropperType, ViewType;
        static readonly MethodInfo StartMethod, EndMethod, PickedMethod, LastPickedMethod, GrabPixelsMethod;
        static readonly PropertyInfo OpenedProperty, CancelledProperty, CurrentViewProperty, MouseOverViewProperty, ScreenPositionProperty, ActualViewProperty;
        static readonly PropertyInfo ViewWindowProperty, ShowModeProperty, WindowsProperty;
        static readonly FieldInfo CoordinatesField, InstanceField, CancelledField, ParentField;
        static readonly object MainWindowMode;
        static readonly List<UnityEngine.Object> s_Views = new List<UnityEngine.Object>();

        sealed class Session
        {
            internal Action<Color> Preview, Picked;
            internal Action Cancelled;
            internal VisualElement Host;
            internal IMGUIContainer Trampoline;
            internal EditorWindow Window;
            internal object View;
            internal Vector2 Mouse, LastCoordinates;
            internal EventCallback<DetachFromPanelEvent> Detach;
            internal bool Launched, Started, Opened, HasSample;
            internal int Frames;
            internal double LastGrab;
        }

        static Session s_Session;
        static bool s_Polling;
        static Texture2D s_Icon, s_GrabRead;
        static bool s_IconPro;
        static RenderTexture s_GrabTarget;

        static ThryEyeDropper()
        {
            // HideAndDontSave objects outlive the domain; free the grab buffers before a reload.
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseGrabBuffers;
            EditorApplication.quitting += ReleaseGrabBuffers;
            var assembly = typeof(Editor).Assembly;
            DropperType = assembly.GetType("UnityEditor.EyeDropper");
            ViewType = assembly.GetType("UnityEditor.GUIView");
            ParentField = typeof(EditorWindow).GetField("m_Parent", AnyInstance);
            try
            {
                // Window order, for choosing between overlapping views in the black-screen fallback.
                ViewWindowProperty = ParentField?.FieldType.GetProperty("window", AnyInstance);
                var containerType = ViewWindowProperty?.PropertyType;
                ShowModeProperty = containerType?.GetProperty("showMode", AnyInstance);
                WindowsProperty = containerType?.GetProperty("windows", AnyStatic);
                if (ShowModeProperty != null && ShowModeProperty.PropertyType.IsEnum) MainWindowMode = Enum.Parse(ShowModeProperty.PropertyType, "MainWindow");
            }
            catch (Exception e) when (e is AmbiguousMatchException || e is ArgumentException) { ShowModeProperty = null; WindowsProperty = null; }
            if (DropperType == null || ViewType == null) return;
            try
            {
                StartMethod = DropperType.GetMethod("Start", AnyStatic, null, new[] { ViewType }, null);
                EndMethod = DropperType.GetMethod("End", AnyStatic, null, Type.EmptyTypes, null);
                PickedMethod = DropperType.GetMethod("GetPickedColor", AnyStatic, null, Type.EmptyTypes, null);
                LastPickedMethod = DropperType.GetMethod("GetLastPickedColor", AnyStatic, null, Type.EmptyTypes, null);
                OpenedProperty = DropperType.GetProperty("IsOpened", AnyStatic);
                CancelledProperty = DropperType.GetProperty("IsCancelled", AnyStatic);
                CoordinatesField = DropperType.GetField("s_PickCoordinates", AnyStatic);
                InstanceField = DropperType.GetField("s_Instance", AnyStatic);
                CancelledField = DropperType.GetField("m_IsCancelled", AnyInstance);
                CurrentViewProperty = ViewType.GetProperty("current", AnyStatic);
                MouseOverViewProperty = ViewType.GetProperty("mouseOverView", AnyStatic);
                ScreenPositionProperty = ViewType.GetProperty("screenPosition", AnyInstance);
                GrabPixelsMethod = ViewType.GetMethod("GrabPixels", AnyInstance, null, new[] { typeof(RenderTexture), typeof(Rect) }, null);
                ActualViewProperty = ParentField?.FieldType.GetProperty("actualView", AnyInstance);
            }
            catch (AmbiguousMatchException) { StartMethod = null; }
        }

        /// <summary>Reflection found Unity's eyedropper.</summary>
        internal static bool Available => StartMethod != null && EndMethod != null && PickedMethod != null && LastPickedMethod != null
            && OpenedProperty != null && ParentField != null;

        internal static bool IsActive => s_Session != null;

        internal static Texture2D Icon
        {
            get
            {
                bool pro = EditorGUIUtility.isProSkin;
                if (s_Icon == null || s_IconPro != pro) { s_Icon = EditorGUIUtility.IconContent("EyeDropper.Large")?.image as Texture2D; s_IconPro = pro; }
                return s_Icon;
            }
        }

        /// <summary>
        /// Starts a pick for a UI Toolkit host. <paramref name="preview"/> receives the color under the cursor,
        /// <paramref name="picked"/> the clicked color. Escape, <see cref="Cancel"/> or the host leaving its panel cancel.
        /// </summary>
        internal static void Begin(VisualElement host, Action<Color> preview, Action<Color> picked, Action cancelled)
        {
            Cancel();
            var panel = host?.panel;
            var window = panel == null || !Available ? null : WindowOf(panel);
            var view = window != null ? ParentField.GetValue(window) : null;
            if (view == null) { cancelled?.Invoke(); return; }
            var trampoline = Trampoline(panel);
            var session = new Session
            {
                Preview = preview, Picked = picked, Cancelled = cancelled, Host = host, Trampoline = trampoline, Window = window, View = view,
                Mouse = trampoline.userData is Vector2 mouse ? mouse : host.worldBound.center
            };
            session.Detach = e => Finish(session, false, default, true);
            host.RegisterCallback(session.Detach);
            s_Session = session;
            // 2022.3's EyeDropper.Start reads Event.current, which UI Toolkit callbacks do not have.
            // The next editor update sends an IMGUI command to the hidden container, which starts it.
            EnsurePolling();
        }

        /// <summary>Same as <see cref="Begin"/> for IMGUI callers, during a MouseDown or ExecuteCommand pass.</summary>
        internal static void BeginFromIMGUI(Action<Color> preview, Action<Color> picked, Action cancelled)
        {
            Cancel();
            var view = Available && Event.current != null ? CurrentViewProperty?.GetValue(null) : null;
            if (view == null) { cancelled?.Invoke(); return; }
            var session = new Session { Preview = preview, Picked = picked, Cancelled = cancelled, View = view, Launched = true, Started = true };
            s_Session = session;
            EnsurePolling();
            try { StartMethod.Invoke(null, new[] { view }); session.Opened = true; }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); Finish(session, false, default, true); }
        }

        internal static void Cancel()
        {
            var session = s_Session;
            if (session != null) Finish(session, false, default, true);
        }

        static void Finish(Session session, bool picked, Color color, bool endDropper = false)
        {
            if (s_Session != session) return;
            s_Session = null;
            session.Host?.UnregisterCallback(session.Detach);
            if (endDropper && session.Opened && IsOpened())
            {
                try { EndMethod.Invoke(null, null); }
                catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
            }
            try
            {
                if (picked) { color.a = 1; session.Picked?.Invoke(color); }
                else session.Cancelled?.Invoke();
            }
            catch (Exception e) when (!(e is ExitGUIException)) { Debug.LogException(e); }
        }

        static void EnsurePolling()
        {
            if (s_Polling) return;
            s_Polling = true;
            EditorApplication.update += Poll;
        }

        // Safety net: commands can miss a host whose view went away, and IMGUI sessions have no commands at all.
        static void Poll()
        {
            var session = s_Session;
            if (session == null) { EditorApplication.update -= Poll; s_Polling = false; return; }
            if (!session.Launched) { Launch(session); return; }
            if (!session.Opened) { if (++session.Frames > 60) Finish(session, false, default); return; }
            if (IsOpened()) { UpdatePreview(session); return; }
            if (WasClicked()) Finish(session, true, Sample(ReadColor(LastPickedMethod)));
            else Finish(session, false, default);
        }

        static void Launch(Session session)
        {
            session.Launched = true;
            var panel = session.Host?.panel;
            if (panel == null || session.Window == null || session.Trampoline.panel != panel) { Finish(session, false, default); return; }
            var focused = panel.focusController?.focusedElement;
            session.Trampoline.Focus();
            try
            {
                var command = EditorGUIUtility.CommandEvent(StartCommand);
                command.mousePosition = session.Mouse;
                session.Window.SendEvent(command);
            }
            finally
            {
                if (session.Trampoline.focusController?.focusedElement == session.Trampoline)
                {
                    if (focused is VisualElement previous && previous != session.Trampoline && previous.panel == panel) previous.Focus();
                    else session.Trampoline.Blur();
                }
            }
            if (s_Session == session && !session.Opened) Finish(session, false, default);
        }

        static IMGUIContainer Trampoline(IPanel panel)
        {
            var tree = panel.visualTree;
            var existing = tree.Q<IMGUIContainer>(TrampolineName);
            if (existing != null) return existing;
            var container = new IMGUIContainer { name = TrampolineName, focusable = true, tabIndex = -1, pickingMode = PickingMode.Ignore };
            container.style.position = Position.Absolute;
            container.style.left = 0; container.style.top = 0; container.style.width = 0; container.style.height = 0;
            container.onGUIHandler = () => OnTrampolineGUI(container);
            // Where the last click happened, so the eyedropper opens under the cursor.
            tree.RegisterCallback<PointerDownEvent>(e => container.userData = (Vector2)e.position, TrickleDown.TrickleDown);
            // First in the tree, so commands reach it before any other IMGUI content in the panel.
            tree.Insert(0, container);
            return container;
        }

        static void OnTrampolineGUI(IMGUIContainer container)
        {
            var e = Event.current;
            var session = s_Session;
            if (e == null || e.type != EventType.ExecuteCommand || session == null || session.Trampoline != container) return;
            switch (e.commandName)
            {
                case StartCommand:
                    if (session.Started) return;
                    session.Started = true;
                    try { StartMethod.Invoke(null, new[] { session.View }); session.Opened = true; }
                    catch (TargetInvocationException ex) { Debug.LogException(ex.InnerException ?? ex); }
                    e.Use();
                    break;
                case "EyeDropperUpdate":
                    if (!session.Opened) return;
                    UpdatePreview(session);
                    e.Use();
                    break;
                case "EyeDropperClicked":
                    if (!session.Opened) return;
                    e.Use();
                    Finish(session, true, Sample(ReadColor(LastPickedMethod)));
                    break;
                case "EyeDropperCancelled":
                    if (!session.Started) return;
                    e.Use();
                    Finish(session, false, default);
                    break;
            }
        }

        static void UpdatePreview(Session session)
        {
            var coordinates = Coordinates();
            if (session.HasSample && coordinates == session.LastCoordinates) return;
            var color = ReadColor(PickedMethod);
            if (IsBlack(color))
            {
                // The fallback reads GPU pixels; keep it to about 20 reads a second while hovering.
                double now = EditorApplication.timeSinceStartup;
                if (session.HasSample && now - session.LastGrab < .05) return;
                session.LastGrab = now;
                if (GrabUnityPixel(coordinates, out var grabbed)) color = grabbed;
            }
            session.LastCoordinates = coordinates; session.HasSample = true;
            color.a = 1;
            try { session.Preview?.Invoke(color); }
            catch (Exception e) when (!(e is ExitGUIException)) { Debug.LogException(e); }
        }

        // Screen reads return black everywhere on some Linux desktops (e.g. KDE Wayland), including over
        // Unity itself. Unity's own views can still be read back, so pure black retries from the view.
        static Color Sample(Color screen)
        {
            if (IsBlack(screen) && GrabUnityPixel(Coordinates(), out var grabbed)) return grabbed;
            return screen;
        }

        static bool IsBlack(Color c) => c.r == 0f && c.g == 0f && c.b == 0f;

        static bool IsOpened()
        {
            try { return OpenedProperty != null && (bool)OpenedProperty.GetValue(null); }
            catch (TargetInvocationException) { return false; }
        }

        // End() clears the instance without marking it cancelled; a click keeps it and leaves it uncancelled.
        static bool WasClicked()
        {
            try
            {
                if (InstanceField == null || CancelledField == null) return CancelledProperty != null && !(bool)CancelledProperty.GetValue(null);
                var instance = InstanceField.GetValue(null);
                return instance != null && !(bool)CancelledField.GetValue(instance);
            }
            catch (TargetInvocationException) { return false; }
        }

        static Color ReadColor(MethodInfo method)
        {
            try { return method != null ? (Color)method.Invoke(null, null) : Color.black; }
            catch (TargetInvocationException) { return Color.black; }
        }

        static Vector2 Coordinates() => CoordinatesField?.GetValue(null) is Vector2 point ? point : Vector2.zero;

        static EditorWindow WindowOf(IPanel panel)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window != null && ParentField.GetValue(window) != null && window.rootVisualElement.panel == panel) return window;
            return null;
        }

        static bool GrabUnityPixel(Vector2 point, out Color color)
        {
            color = Color.black;
            if (GrabPixelsMethod == null || ScreenPositionProperty == null) return false;
            var view = ViewAt(point);
            if (view == null) return false;
            var screen = (Rect)ScreenPositionProperty.GetValue(view);
            float ppp = EditorGUIUtility.pixelsPerPoint;
            int height = Mathf.RoundToInt(screen.height * ppp), width = Mathf.RoundToInt(screen.width * ppp);
            int x = Mathf.FloorToInt((point.x - screen.x) * ppp), y = Mathf.FloorToInt((point.y - screen.y) * ppp);
            if (height <= 0 || x < 0 || y < 0 || x >= width || y >= height) return false;
            if (s_GrabTarget == null || s_GrabTarget.height != height)
            {
                if (s_GrabTarget != null) { s_GrabTarget.Release(); UnityEngine.Object.DestroyImmediate(s_GrabTarget); }
                s_GrabTarget = new RenderTexture(1, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave };
                s_GrabTarget.Create();
            }
            if (s_GrabRead == null) s_GrabRead = new Texture2D(1, height, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            else if (s_GrabRead.height != height) s_GrabRead.Reinitialize(1, height);
            var active = RenderTexture.active;
            try
            {
                // One full-height column, so only the column's x has to be right.
                GrabPixelsMethod.Invoke(view, new object[] { s_GrabTarget, new Rect(x, 0, 1, height) });
                RenderTexture.active = s_GrabTarget;
                s_GrabRead.ReadPixels(new Rect(0, 0, 1, height), 0, 0, false);
                // GUIView captures are top-down: row 0 is the top of the view.
                color = s_GrabRead.GetPixel(0, y);
                color.a = 1;
                return true;
            }
            catch (TargetInvocationException) { return false; }
            finally { RenderTexture.active = active; }
        }

        static void ReleaseGrabBuffers()
        {
            if (s_GrabTarget != null) { s_GrabTarget.Release(); UnityEngine.Object.DestroyImmediate(s_GrabTarget); }
            if (s_GrabRead != null) UnityEngine.Object.DestroyImmediate(s_GrabRead);
            s_GrabTarget = null; s_GrabRead = null;
        }

        static object ViewAt(Vector2 point)
        {
            var over = MouseOverViewProperty?.GetValue(null) as UnityEngine.Object;
            if (over != null && !DropperType.IsInstanceOfType(over) && ViewContains(over, point)) return over;
            s_Views.Clear();
            try
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                {
                    var parent = window != null ? ParentField.GetValue(window) as UnityEngine.Object : null;
                    if (parent == null || DropperType.IsInstanceOfType(parent)) continue;
                    if (ActualViewProperty != null && !ReferenceEquals(ActualViewProperty.GetValue(parent), window)) continue;
                    if (ViewContains(parent, point) && !s_Views.Contains(parent)) s_Views.Add(parent);
                }
                return FrontView();
            }
            catch (TargetInvocationException) { return null; }
            finally { s_Views.Clear(); }
        }

        // Where windows overlap, only the front one is visible, and the order of FindObjectsOfTypeAll says
        // nothing about that. Floating windows are above the main window's docked views; between floating
        // windows only Unity's window list can tell. Without it, reading a hidden view is worse than no read.
        static object FrontView()
        {
            if (s_Views.Count <= 1) return s_Views.Count == 1 ? s_Views[0] : null;
            if (ViewWindowProperty == null) return null;
            if (ShowModeProperty != null && MainWindowMode != null)
            {
                int floating = 0;
                foreach (var view in s_Views) if (!IsMainWindowView(view)) floating++;
                if (floating > 0)
                    for (int i = s_Views.Count - 1; i >= 0; i--)
                        if (IsMainWindowView(s_Views[i])) s_Views.RemoveAt(i);
                if (s_Views.Count == 1) return s_Views[0];
            }
            // ContainerWindow.windows lists windows front to back.
            if (!(WindowsProperty?.GetValue(null) is Array windows)) return null;
            foreach (var container in windows)
                foreach (var view in s_Views)
                    if (container != null && ReferenceEquals(ViewWindowProperty.GetValue(view), container)) return view;
            return null;
        }

        static bool IsMainWindowView(object view)
        {
            var container = ViewWindowProperty.GetValue(view);
            return container != null && Equals(ShowModeProperty.GetValue(container), MainWindowMode);
        }

        static bool ViewContains(object view, Vector2 point)
        {
            try { return ScreenPositionProperty.GetValue(view) is Rect rect && rect.Contains(point); }
            catch (TargetInvocationException) { return false; }
        }
    }
}
