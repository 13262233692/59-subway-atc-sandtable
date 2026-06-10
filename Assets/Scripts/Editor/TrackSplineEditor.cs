using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using CBTC.Sandtable.Track;

namespace CBTC.Sandtable.Editor
{
    [CustomEditor(typeof(CubicBezierSpline))]
    public class TrackSplineEditor : UnityEditor.Editor
    {
        private int _selectedPointIndex = -1;
        private bool _showSpeedRestrictions = true;

        public override void OnInspectorGUI()
        {
            CubicBezierSpline spline = (CubicBezierSpline)target;

            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Control Points", EditorStyles.boldLabel);

            List<SplineControlPoint> points = spline.ControlPoints;
            for (int i = 0; i < points.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Point {i}", GUILayout.Width(60));

                Color prevColor = GUI.color;
                if (i == _selectedPointIndex) GUI.color = Color.cyan;

                Vector3 pos = points[i].position;
                Vector3 newPos = EditorGUILayout.Vector3Field("", pos);
                if (newPos != pos)
                {
                    Undo.RecordObject(spline, "Move Control Point");
                    spline.SetPointPosition(i, newPos);
                }

                GUI.color = prevColor;

                if (GUILayout.Button("Select", GUILayout.Width(50)))
                {
                    _selectedPointIndex = i;
                    SceneView.Frame(new Bounds(newPos, Vector3.one * 5f), false);
                }

                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    Undo.RecordObject(spline, "Remove Control Point");
                    spline.RemovePoint(i);
                    if (_selectedPointIndex >= points.Count)
                        _selectedPointIndex = points.Count - 1;
                    EditorUtility.SetDirty(spline);
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Point"))
            {
                Undo.RecordObject(spline, "Add Control Point");
                Vector3 addPos = points.Count > 0
                    ? points[points.Count - 1].position + Vector3.right * 10f
                    : Vector3.zero;
                spline.AddPoint(addPos);
                _selectedPointIndex = points.Count - 1;
                EditorUtility.SetDirty(spline);
            }

            if (GUILayout.Button("Recalculate"))
            {
                Undo.RecordObject(spline, "Recalculate Spline");
                spline.Recalculate();
                EditorUtility.SetDirty(spline);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Spline Info", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Total Length:", spline.TotalLength.ToString("F2") + " m");
            EditorGUILayout.LabelField("Segment Count:", spline.SegmentCount.ToString());
            EditorGUILayout.LabelField("Point Count:", spline.PointCount.ToString());

            if (_selectedPointIndex >= 0 && _selectedPointIndex < points.Count)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"Selected Point {_selectedPointIndex}", EditorStyles.boldLabel);
                float t = (float)_selectedPointIndex / Mathf.Max(1, spline.SegmentCount);
                t = Mathf.Clamp01(t);
                float curvature = spline.GetCurvatureAt(t);
                float radius = curvature > Mathf.Epsilon ? 1f / curvature : float.MaxValue;
                EditorGUILayout.LabelField("Curvature:", curvature.ToString("F6"));
                EditorGUILayout.LabelField("Curve Radius:", radius < float.MaxValue ? radius.ToString("F2") + " m" : "∞");

                float dist = spline.GetDistanceFromParameter(t);
                EditorGUILayout.LabelField("Arc Length:", dist.ToString("F2") + " m");
            }

            _showSpeedRestrictions = EditorGUILayout.Foldout(_showSpeedRestrictions, "Speed Restrictions");
            if (_showSpeedRestrictions && spline.ControlPoints.Count > 1)
            {
                DrawSpeedRestrictionInfo(spline);
            }
        }

        private void DrawSpeedRestrictionInfo(CubicBezierSpline spline)
        {
            int sampleCount = 50;
            for (int i = 0; i <= sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float curvature = spline.GetCurvatureAt(t);
                if (curvature > 0.005f)
                {
                    float radius = 1f / curvature;
                    float dist = spline.GetDistanceFromParameter(t);
                    EditorGUILayout.LabelField($"  t={t:F2} dist={dist:F1}m  R={radius:F0}m  curve");
                }
            }
        }

        protected void OnSceneGUI()
        {
            CubicBezierSpline spline = (CubicBezierSpline)target;
            List<SplineControlPoint> points = spline.ControlPoints;

            if (points.Count < 2) return;

            DrawSplineCurve(spline);
            DrawControlPoints(spline, points);
            DrawTangentHandles(spline, points);
            DrawArcLengthAndCurvatureLabels(spline);
            DrawSpeedRestrictionOverlay(spline);
        }

        private void DrawSplineCurve(CubicBezierSpline spline)
        {
            int sampleCount = 200;
            Vector3[] sampledPoints = new Vector3[sampleCount + 1];
            for (int i = 0; i <= sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                sampledPoints[i] = spline.GetPoint(t);
            }

            Handles.DrawPolyLine(sampledPoints);

            for (int i = 0; i <= sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float curvature = spline.GetCurvatureAt(t);
                if (curvature > 0.01f)
                {
                    float radius = 1f / curvature;
                    if (radius < 300f)
                    {
                        Color prevColor = Handles.color;
                        float hue = Mathf.Clamp01(radius / 300f);
                        Handles.color = Color.Lerp(Color.red, Color.green, hue);
                        if (i > 0)
                        {
                            Handles.DrawLine(sampledPoints[i - 1], sampledPoints[i], 3f);
                        }
                        Handles.color = prevColor;
                    }
                }
            }
        }

        private void DrawControlPoints(CubicBezierSpline spline, List<SplineControlPoint> points)
        {
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 pos = points[i].position;
                float size = HandleUtility.GetHandleSize(pos) * 0.1f;

                if (i % 3 == 0)
                {
                    Handles.color = Color.white;
                }
                else
                {
                    Handles.color = Color.gray;
                }

                if (i == _selectedPointIndex)
                {
                    Handles.color = Color.cyan;
                }

                Handles.SphereHandleCap(0, pos, Quaternion.identity, size, EventType.Repaint);

                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.PositionHandle(pos, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(spline, "Move Control Point");
                    spline.SetPointPosition(i, newPos);
                    EditorUtility.SetDirty(spline);
                }

                Handles.Label(pos + Vector3.up * size * 2f, $"P{i}");
            }
        }

        private void DrawTangentHandles(CubicBezierSpline spline, List<SplineControlPoint> points)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (i % 3 == 0) continue;

                SplineControlPoint cp = points[i];

                Handles.color = Color.blue;
                Handles.DrawLine(cp.inTangent, cp.position, 1.5f);
                EditorGUI.BeginChangeCheck();
                Vector3 newInTangent = Handles.PositionHandle(cp.inTangent, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(spline, "Move In Tangent");
                    cp.inTangent = newInTangent;
                    points[i] = cp;
                    spline.MarkDirty();
                    EditorUtility.SetDirty(spline);
                }

                Handles.color = Color.green;
                Handles.DrawLine(cp.position, cp.outTangent, 1.5f);
                EditorGUI.BeginChangeCheck();
                Vector3 newOutTangent = Handles.PositionHandle(cp.outTangent, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(spline, "Move Out Tangent");
                    cp.outTangent = newOutTangent;
                    points[i] = cp;
                    spline.MarkDirty();
                    EditorUtility.SetDirty(spline);
                }
            }
        }

        private void DrawArcLengthAndCurvatureLabels(CubicBezierSpline spline)
        {
            if (spline.SegmentCount < 1) return;

            Handles.color = Color.yellow;
            int labelCount = 10;
            for (int i = 0; i <= labelCount; i++)
            {
                float t = (float)i / labelCount;
                Vector3 pos = spline.GetPoint(t);
                float dist = spline.GetDistanceFromParameter(t);
                float curvature = spline.GetCurvatureAt(t);
                float radius = curvature > Mathf.Epsilon ? 1f / curvature : float.MaxValue;

                string label = $"{dist:F0}m";
                if (curvature > 0.001f && radius < 5000f)
                {
                    label += $" R={radius:F0}m";
                }

                Handles.Label(pos + Vector3.up * 2f, label);
            }
        }

        private void DrawSpeedRestrictionOverlay(CubicBezierSpline spline)
        {
            if (spline.SegmentCount < 1) return;

            int sampleCount = 100;
            for (int i = 0; i < sampleCount; i++)
            {
                float t0 = (float)i / sampleCount;
                float t1 = (float)(i + 1) / sampleCount;

                float curvature0 = spline.GetCurvatureAt(t0);

                if (curvature0 > 0.01f)
                {
                    float radius = 1f / curvature0;
                    if (radius < 200f)
                    {
                        Vector3 p0 = spline.GetPoint(t0);
                        Vector3 p1 = spline.GetPoint(t1);
                        Vector3 tangent = spline.GetTangent(t0);
                        Vector3 normal = spline.GetNormal(t0);

                        Color prevColor = Handles.color;
                        Handles.color = new Color(1f, 0.5f, 0f, 0.6f);
                        Handles.DrawLine(p0 + normal * 2f, p1 + normal * 2f, 3f);
                        Handles.DrawLine(p0 - normal * 2f, p1 - normal * 2f, 3f);
                        Handles.color = prevColor;
                    }
                }
            }
        }
    }
}
