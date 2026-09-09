using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace Thry.ThryEditor
{
    public class DecalSceneTool
    {
        private MaterialProperty _propPosition;
        private MaterialProperty _propRotation;
        private MaterialProperty _propScale;
        private MaterialProperty _propOffset;
        private Material _material;
        private Renderer _renderer;
        private int _uvIndex;
        private Mesh _mesh;
        private Vector2[][] _uvTriangles;
        private Vector3[][] _worldTriangles;
        private Vector3[][] _worldNormals;
        private bool _isActive;
        private Mode _mode = Mode.None;
        private HandleMode _handleMode = HandleMode.Position;
        private Tool _previousTool;
        private readonly List<PositioningValue> _initialValues = new List<PositioningValue>();

        private sealed class PositioningValue
        {
            internal Material Material;
            internal Shader Shader;
            internal string Name;
            internal bool Vector;
            internal Vector4 Value;
            internal bool Available => Material != null && Material.shader == Shader && Material.HasProperty(Name);
            internal Vector4 Read() => Vector ? Material.GetVector(Name) : new Vector4(Material.GetFloat(Name), 0, 0, 0);
            internal void Write(Vector4 value)
            {
                if (!Available) return;
                if (Vector) Material.SetVector(Name, value); else Material.SetFloat(Name, value.x);
                EditorUtility.SetDirty(Material);
            }
        }

        public enum Mode
        {
            None, Raycast, Handles
        }

        public enum HandleMode
        {
            Position, Rotation, Scale, Offset
        }

        private DecalSceneTool() {}

        public static DecalSceneTool Create(Renderer renderer, Material m, int uvIndex, MaterialProperty propPosition, MaterialProperty propRotation, MaterialProperty propScale, MaterialProperty propOffset)
        {
            var tool = new DecalSceneTool();
            tool._material = m;
            tool._uvIndex = uvIndex;
            tool._propPosition = propPosition;
            tool._propRotation = propRotation;
            tool._propScale = propScale;
            tool._propOffset = propOffset;
            tool._renderer = renderer;
            try { tool.Init(); }
            finally
            {
                if (renderer is SkinnedMeshRenderer && tool._mesh != null) Object.DestroyImmediate(tool._mesh);
                tool._mesh = null;
            }
            return tool;
        }

        public void SetMaterialProperties(MaterialProperty propPosition, MaterialProperty propRotation, MaterialProperty propScale, MaterialProperty propOffset)
        {
            _propPosition = propPosition;
            _propRotation = propRotation;
            _propScale = propScale;
            _propOffset = propOffset;
        }

        public void StartRaycastMode()
        {
            _mode = Mode.Raycast;
            this.Activate();
        }

        public void StartHandleMode()
        {
            _mode = Mode.Handles;
            this.Activate();
        }

        public void Activate()
        {
            if(_isActive) return;
            _previousTool = Tools.current;
            Tools.current = Tool.None;
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChange;
            _isActive = true;

            _initialValues.Clear();
            foreach (var property in new[] { _propPosition, _propRotation, _propScale, _propOffset })
            {
                if (property == null) continue;
                foreach (var material in property.targets.OfType<Material>())
                {
                    if (material == null || !material.HasProperty(property.name)
                        || _initialValues.Any(v => v.Material == material && v.Name == property.name)) continue;
                    var value = new PositioningValue { Material = material, Shader = material.shader, Name = property.name,
                        Vector = property.type == MaterialProperty.PropType.Vector };
                    value.Value = value.Read(); _initialValues.Add(value);
                }
            }
        }

        public void Deactivate(bool discardChanges) 
        {
            if(!_isActive) return;
            SceneView.duringSceneGui -= OnSceneGUI;
            Selection.selectionChanged -= OnSelectionChange;
            Tools.current = _previousTool;
            _isActive = false;

            // Finish any unrelated pending records before restoring this tool's preview.
            // A scene tool can stay open across many independent inspector edits.
            Undo.FlushUndoRecordObjects();
            var changed = _initialValues.Where(v => v.Available && v.Read() != v.Value).ToArray();
            var finalValues = changed.Select(v => v.Read()).ToArray();
            foreach (var value in changed) value.Write(value.Value);
            if (!discardChanges && changed.Length > 0)
            {
                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(changed.Select(v => v.Material).Distinct().ToArray(),
                    _mode == Mode.Raycast ? "Apply Decal Raycast Tool" : "Apply Decal Scene Tool");
                for (int i = 0; i < changed.Length; i++) changed[i].Write(finalValues[i]);
                Undo.IncrementCurrentGroup();
            }
            _initialValues.Clear();
            _mode = Mode.None;
            SceneView.RepaintAll();
        }

        public Mode GetMode()
        {
            return _mode;
        }

        void OnSelectionChange() 
        {
            this.Deactivate(_mode == Mode.Raycast);
        }

        void Init()
        {
            GetMesh();

            if (_mesh == null) throw new System.InvalidOperationException("The renderer has no mesh for decal positioning.");

            int meshTriangleLength = _mesh.triangles.Length;
            _uvTriangles = new Vector2[meshTriangleLength / 3][];
            _worldTriangles = new Vector3[meshTriangleLength / 3][];
            _worldNormals = new Vector3[meshTriangleLength / 3][];
            int[] triangles = _mesh.triangles;
            Vector2[] uvs;
            if(_uvIndex == 1) uvs = _mesh.uv2;
            else if(_uvIndex == 2) uvs = _mesh.uv3;
            else if(_uvIndex == 3) uvs = _mesh.uv4;
            else uvs = _mesh.uv;
            Vector3[] vertices = _mesh.vertices;
            if (uvs.Length != vertices.Length) throw new System.InvalidOperationException("The mesh does not contain the selected UV channel.");
            Transform root = _renderer.transform;
            Vector3 inverseScale = new Vector3(1.0f / root.lossyScale.x, 1.0f / root.lossyScale.y, 1.0f / root.lossyScale.z);
            bool isSMR = _renderer is SkinnedMeshRenderer;

            Vector3[] meshNormals = _mesh.normals; // Repeatedly accessing _mesh.normals in a loop is veeeery slow
            if (meshNormals.Length != vertices.Length) meshNormals = Enumerable.Repeat(Vector3.up, vertices.Length).ToArray();
            
            for(int i = 0; i < triangles.Length; i += 3)
            {
                _uvTriangles[i / 3] = new Vector2[3];
                _worldTriangles[i / 3] = new Vector3[3];
                _worldNormals[i / 3] = new Vector3[3];
                for (int j = 0; j < 3; j++)
                {
                    _uvTriangles[i / 3][j] = uvs[triangles[i + j]];
                    if(isSMR)
                    {
                        _worldTriangles[i / 3][j] = root.TransformPoint(Vector3.Scale(vertices[triangles[i + j]], inverseScale));
                        _worldNormals[i / 3][j] = root.TransformDirection(meshNormals[triangles[i + j]]);
                    }
                    else
                    {
                        _worldTriangles[i / 3][j] = root.TransformPoint(vertices[triangles[i + j]]);
                        _worldNormals[i / 3][j] = root.TransformDirection(meshNormals[triangles[i + j]]);
                    }
                }
            }
        }

        private void OnSceneGUI(SceneView sceneView) 
        {
            if (_initialValues.Any(value => !value.Available))
            {
                Deactivate(_mode == Mode.Raycast);
                return;
            }
            if(Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Escape)
            {
                Event.current.Use();
                Deactivate(true);
                return;
            }
            switch(_mode)
            {
                case Mode.Raycast:
                    RaycastMode(sceneView);
                    break;
                case Mode.Handles:
                    HandlesMode(sceneView);
                    break;
            }
        }

        void RaycastMode(SceneView sceneView)
        {
            if(Tools.current != Tool.View)
            {
                Tools.current = Tool.View;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

            Vector2 uv = _propPosition.vectorValue;
            if(RaycastToClosestUV(ray, ref uv))
            {
                _propPosition.vectorValue = uv;
            }

            if(Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                Deactivate(false);
            }
        }

        void HandlesMode(SceneView sceneView)
        {
            switch(_handleMode)
            {
                case HandleMode.Position:
                    PositionMode(sceneView);
                    break;
                case HandleMode.Rotation:
                    RotationMode(sceneView);
                    break;
                case HandleMode.Scale:
                    ScaleMode(sceneView);
                    break;
                case HandleMode.Offset:
                    OffsetMode(sceneView);
                    break;
            }

            if(Tools.current != Tool.None)
            {
                switch(Tools.current)
                {
                    case Tool.Move:
                        _handleMode = HandleMode.Position;
                        break;
                    case Tool.Rotate:
                        _handleMode = HandleMode.Rotation;
                        break;
                    case Tool.Scale:
                        _handleMode = HandleMode.Scale;
                        break;
                    case Tool.Rect:
                        _handleMode = HandleMode.Offset;
                        break;
                }
                Tools.current = Tool.None;
            }
        }

        void PositionMode(SceneView sceneView)
        {
            GetPivot();
            Vector3 gizmoNormal = _pivotNormal;
            if(Vector3.Dot(sceneView.camera.transform.forward, _pivotNormal) < 0)
            {
                gizmoNormal = -_pivotNormal;
            }

            Vector3 up = _pivotUp;
            if (Tools.pivotRotation == PivotRotation.Local)
            {
                up = Quaternion.AngleAxis(-_propRotation.floatValue, gizmoNormal) * up;
            }

            Quaternion rotation = Quaternion.LookRotation(gizmoNormal, up);

            Vector3 moved = Handles.PositionHandle(_pivotPoint, rotation);
            if(moved != _pivotPoint)
            {
                Vector2 uv = Vector2.zero;
                Ray ray = new Ray(moved - gizmoNormal * 0.1f, gizmoNormal);
                if(RaycastToClosestUV(ray, ref uv))
                {
                    _propPosition.vectorValue = uv;
                }
            }
        }

        void RotationMode(SceneView sceneView)
        {
            GetPivot();
            Vector3 gizmoNormal = _pivotNormal;
            if(Vector3.Dot(sceneView.camera.transform.forward, _pivotNormal) < 0)
            {
                gizmoNormal = -_pivotNormal;
            }
            Quaternion rotation = Quaternion.LookRotation(gizmoNormal, _pivotUp);
            rotation *= Quaternion.Euler(0, 0, -_propRotation.floatValue);

            Quaternion moved = Handles.RotationHandle(rotation, _pivotPoint);
            if(moved != rotation)
            {
                Quaternion delta = Quaternion.Inverse(rotation) * moved;
                float deltaAngle = delta.eulerAngles.z;
                SetClampedRotation(_propRotation, _propRotation.floatValue - deltaAngle);
            }
        }

        Vector3 _initalScale;
        void ScaleMode(SceneView sceneView)
        {
            GetPivot();
            Vector3 gizmoNormal = _pivotNormal;
            if(Vector3.Dot(sceneView.camera.transform.forward, _pivotNormal) < 0)
            {
                gizmoNormal = -_pivotNormal;
            }
            Quaternion rotation = Quaternion.LookRotation(gizmoNormal, _pivotUp);
            rotation *= Quaternion.Euler(0, 0, -_propRotation.floatValue);

            if(Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _initalScale = _propScale.vectorValue;
            }

            Vector3 moved = Handles.ScaleHandle(Vector3.one, _pivotPoint, rotation, HandleUtility.GetHandleSize(_pivotPoint));
            if(moved != Vector3.one)
            {
                Vector4 scale = _initalScale;
                scale.x *= moved.x;
                scale.y *= moved.y;
                _propScale.vectorValue = scale;
            }
        }

        Vector4 _initalOffset;
        void OffsetMode(SceneView sceneView)
        {
            GetPivot();
            if(Vector3.Dot(sceneView.camera.transform.forward, _pivotNormal) < 0)
            {
                _pivotNormal = -_pivotNormal;
            }
            Quaternion rotation = Quaternion.LookRotation(_pivotNormal, _pivotUp);
            rotation *= Quaternion.Euler(0, 0, -_propRotation.floatValue);

            if(Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _initalOffset = _propOffset.vectorValue;
            }

            float size = HandleUtility.GetHandleSize(_pivotPoint);
            float left = Handles.ScaleValueHandle(1, _pivotPoint + rotation * Vector3.left * size, rotation * Quaternion.Euler(0, -90, 0), size * 5, Handles.ArrowHandleCap, 0);
            float right = Handles.ScaleValueHandle(1, _pivotPoint + rotation * Vector3.right * size, rotation * Quaternion.Euler(0, 90, 0), size * 5, Handles.ArrowHandleCap, 0);
            float down = Handles.ScaleValueHandle(1, _pivotPoint + rotation * Vector3.down * size, rotation * Quaternion.Euler(90, 0, 0), size * 5, Handles.ArrowHandleCap, 0);
            float up = Handles.ScaleValueHandle(1, _pivotPoint + rotation * Vector3.up * size, rotation * Quaternion.Euler(-90, 0, 0), size * 5, Handles.ArrowHandleCap, 0);
            if(left != 1 || right != 1 || down != 1 || up != 1)
            {
                Vector4 offset = _initalOffset;
                offset.x -= (left - 1) * _propScale.vectorValue.x * 0.25f;
                offset.y += (right - 1) * _propScale.vectorValue.x * 0.25f;
                offset.z -= (down - 1) * _propScale.vectorValue.y * 0.25f;
                offset.w += (up - 1) * _propScale.vectorValue.y * 0.25f;
                _propOffset.vectorValue = offset;
            }
        }

        Vector3 _pivotPoint;
        Vector3 _pivotNormal;
        Vector3 _pivotUp;

        static float TriangleArea3D(Vector3 a, Vector3 b, Vector3 c)
        {
            var v1 = a - c;
            var v2 = b - c;
            return Vector3.Cross(v1, v2).magnitude / 2;
        }

        void GetPivot()
        {
            _pivotPoint = Vector3.zero;
            _pivotNormal = Vector3.zero;

            Vector2 uv = _propPosition.vectorValue;
            Vector2 uvUp = uv + Vector2.up * 0.0001f;
            // uv position to world position using renderer mesh
            for(int i=0; i<_worldTriangles.Length;i++)
            {
                Vector2[] triUVcoords = _uvTriangles[i];
                float a = TriangleArea(triUVcoords[0], triUVcoords[1], triUVcoords[2]);
                if(a == 0) continue;
                // check if uv is inside uvTriangle
                float a1 = TriangleArea(triUVcoords[1], triUVcoords[2], uv) / a;
                if(a1 < 0) continue;
                float a2 = TriangleArea(triUVcoords[2], triUVcoords[0], uv) / a;
                if(a2 < 0) continue;
                float a3 = TriangleArea(triUVcoords[0], triUVcoords[1], uv) / a;
                if(a3 < 0) continue;
                // get a1, a2, a3 of uv up
                float a1Up = TriangleArea(triUVcoords[1], triUVcoords[2], uvUp) / a;
                float a2Up = TriangleArea(triUVcoords[2], triUVcoords[0], uvUp) / a;
                float a3Up = TriangleArea(triUVcoords[0], triUVcoords[1], uvUp) / a;
                // point inside the triangle - find mesh position by interpolation
                Vector3[] triVertices = _worldTriangles[i];
                Vector3[] triNormals = _worldNormals[i];
                _pivotPoint = triVertices[0] * a1 + triVertices[1] * a2 + triVertices[2] * a3;
                _pivotNormal = triNormals[0] * a1 + triNormals[1] * a2 + triNormals[2] * a3;
                _pivotUp = (triVertices[0] * a1Up + triVertices[1] * a2Up + triVertices[2] * a3Up - _pivotPoint).normalized;
                return;
            }
        }

        bool RaycastToClosestUV(Ray ray, ref Vector2 uv)
        {
            Vector4 scaleOffset = _propOffset.vectorValue;
            scaleOffset = new Vector4(-scaleOffset.x, scaleOffset.y, -scaleOffset.z, scaleOffset.w);
            Vector2 centerOffset = new Vector2((scaleOffset.x + scaleOffset.y)/2, (scaleOffset.z + scaleOffset.w)/2);

            float minDistance = float.MaxValue;
            for(int i=0; i<_worldTriangles.Length;i++)
            {
                Vector3[] triangle = _worldTriangles[i];
                // raycast to triangle
                Plane plane = new Plane(triangle[0], triangle[1], triangle[2]);
                float distance;
                if(plane.Raycast(ray, out distance))
                {
                    Vector3 hitPoint = ray.GetPoint(distance);
                    // check if hitPoint is inside triangle using 3D areas
                    float a = TriangleArea3D(triangle[0], triangle[1], triangle[2]);
                    if(a == 0) continue;
                    float a1 = TriangleArea3D(triangle[1], triangle[2], hitPoint) / a;
                    float a2 = TriangleArea3D(triangle[2], triangle[0], hitPoint) / a;
                    float a3 = TriangleArea3D(triangle[0], triangle[1], hitPoint) / a;
                    if(a1 + a2 + a3 > 1.001f) continue;
                    if(distance < minDistance)
                    {
                        minDistance = distance;
                        // point inside the triangle - find uv by interpolation
                        Vector2[] uvTriangle = _uvTriangles[i];
                        uv = uvTriangle[0] * a1 + uvTriangle[1] * a2 + uvTriangle[2] * a3;
                        uv = uv - centerOffset;
                    }
                }
            }
            return minDistance != float.MaxValue;
        }

        float TriangleArea(Vector2 a, Vector2 b, Vector2 c)
        {
            var v1 = a - c;
            var v2 = b - c;
            return (v1.x * v2.y - v1.y * v2.x) / 2;
        }

        static void SetClampedRotation(MaterialProperty property, float value)
        {
            Vector2 limits = property.rangeLimits;
            value = Helper.Mod(value - limits.x, limits.y - limits.x) + limits.x;
            property.floatValue = value;
        }

        void GetMesh()
        {
            if(_renderer is MeshRenderer)
            {
                _mesh = _renderer.GetComponent<MeshFilter>().sharedMesh;
            }
            else if(_renderer is SkinnedMeshRenderer)
            {
                _mesh = new Mesh();
                (_renderer as SkinnedMeshRenderer).BakeMesh(_mesh);
            }
        }
    }
}
