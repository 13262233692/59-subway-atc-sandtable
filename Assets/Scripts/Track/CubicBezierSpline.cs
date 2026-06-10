using System;
using System.Collections.Generic;
using UnityEngine;

namespace CBTC.Sandtable.Track
{
    [Serializable]
    public struct SplineControlPoint
    {
        public Vector3 position;
        public Vector3 inTangent;
        public Vector3 outTangent;

        public SplineControlPoint(Vector3 pos)
        {
            position = pos;
            inTangent = pos;
            outTangent = pos;
        }

        public void AutoSmooth(float factor = 0.33f)
        {
            outTangent = position + (position - inTangent) * factor;
        }
    }

    [Serializable]
    public class ArcLengthTable
    {
        public List<float> distances = new List<float>();
        public List<float> parameters = new List<float>();
        public float totalLength;

        public void Build(CubicBezierSpline spline, int samplesPerSegment = 64)
        {
            distances.Clear();
            parameters.Clear();

            int segmentCount = spline.SegmentCount;
            if (segmentCount < 1) return;

            distances.Add(0f);
            parameters.Add(0f);

            float accumulated = 0f;
            Vector3 prevPoint = spline.GetPointRaw(0f);

            for (int i = 0; i < segmentCount; i++)
            {
                for (int s = 1; s <= samplesPerSegment; s++)
                {
                    float t = (i + (float)s / samplesPerSegment) / segmentCount;
                    Vector3 currPoint = spline.GetPointRaw(t);
                    accumulated += Vector3.Distance(prevPoint, currPoint);
                    distances.Add(accumulated);
                    parameters.Add(t);
                    prevPoint = currPoint;
                }
            }

            totalLength = accumulated;
        }
    }

    public class CubicBezierSpline : MonoBehaviour
    {
        [SerializeField] private List<SplineControlPoint> _controlPoints = new List<SplineControlPoint>();
        [SerializeField] private bool _autoSmooth = true;
        [SerializeField] private float _smoothFactor = 0.33f;
        [SerializeField] private int _arcLengthSamples = 64;

        private ArcLengthTable _arcLengthTable = new ArcLengthTable();
        private bool _dirty = true;

        public List<SplineControlPoint> ControlPoints => _controlPoints;
        public int PointCount => _controlPoints.Count;
        public int SegmentCount => Mathf.Max(0, (_controlPoints.Count - 1) / 3);
        public float TotalLength
        {
            get
            {
                EnsureArcLengthTable();
                return _arcLengthTable.totalLength;
            }
        }

        public void AddPoint(Vector3 position)
        {
            SplineControlPoint cp = new SplineControlPoint(position);
            if (_controlPoints.Count > 0)
            {
                Vector3 lastPos = _controlPoints[_controlPoints.Count - 1].position;
                Vector3 dir = (position - lastPos).normalized;
                float dist = Vector3.Distance(position, lastPos);
                cp.inTangent = position - dir * dist * 0.33f;
                cp.outTangent = position + dir * dist * 0.33f;
            }
            _controlPoints.Add(cp);
            _dirty = true;
        }

        public void InsertPoint(int index, Vector3 position)
        {
            _controlPoints.Insert(index, new SplineControlPoint(position));
            _dirty = true;
        }

        public void RemovePoint(int index)
        {
            if (index >= 0 && index < _controlPoints.Count)
            {
                _controlPoints.RemoveAt(index);
                _dirty = true;
            }
        }

        public void SetPointPosition(int index, Vector3 position)
        {
            if (index < 0 || index >= _controlPoints.Count) return;
            SplineControlPoint cp = _controlPoints[index];
            Vector3 delta = position - cp.position;
            cp.position = position;
            cp.inTangent += delta;
            cp.outTangent += delta;
            _controlPoints[index] = cp;
            _dirty = true;
        }

        public void MarkDirty()
        {
            _dirty = true;
        }

        public void Recalculate()
        {
            if (_autoSmooth)
            {
                for (int i = 0; i < _controlPoints.Count; i++)
                {
                    SplineControlPoint cp = _controlPoints[i];
                    if (i > 0 && i < _controlPoints.Count - 1)
                    {
                        Vector3 prev = _controlPoints[i - 1].position;
                        Vector3 next = _controlPoints[i + 1].position;
                        Vector3 dir = (next - prev).normalized;
                        float dist = Mathf.Min(Vector3.Distance(prev, cp.position), Vector3.Distance(cp.position, next));
                        cp.inTangent = cp.position - dir * dist * _smoothFactor;
                        cp.outTangent = cp.position + dir * dist * _smoothFactor;
                        _controlPoints[i] = cp;
                    }
                    else if (i == 0 && _controlPoints.Count > 1)
                    {
                        Vector3 next = _controlPoints[1].position;
                        float dist = Vector3.Distance(cp.position, next);
                        cp.outTangent = cp.position + (next - cp.position).normalized * dist * _smoothFactor;
                        _controlPoints[i] = cp;
                    }
                    else if (i == _controlPoints.Count - 1 && _controlPoints.Count > 1)
                    {
                        Vector3 prev = _controlPoints[i - 1].position;
                        float dist = Vector3.Distance(cp.position, prev);
                        cp.inTangent = cp.position - (cp.position - prev).normalized * dist * _smoothFactor;
                        _controlPoints[i] = cp;
                    }
                }
            }
            _arcLengthTable.Build(this, _arcLengthSamples);
            _dirty = false;
        }

        private void EnsureArcLengthTable()
        {
            if (_dirty)
                Recalculate();
        }

        public Vector3 GetPointRaw(float t)
        {
            int segCount = SegmentCount;
            if (segCount < 1) return _controlPoints.Count > 0 ? _controlPoints[0].position : Vector3.zero;
            t = Mathf.Clamp01(t);
            float scaledT = t * segCount;
            int segIndex = Mathf.Min(Mathf.FloorToInt(scaledT), segCount - 1);
            float localT = scaledT - segIndex;

            int i = segIndex * 3;
            Vector3 p0 = _controlPoints[i].position;
            Vector3 p1 = _controlPoints[i + 1].position;
            Vector3 p2 = _controlPoints[i + 2].position;
            Vector3 p3 = _controlPoints[i + 3].position;

            float u = 1f - localT;
            return u * u * u * p0 + 3f * u * u * localT * p1 + 3f * u * localT * localT * p2 + localT * localT * localT * p3;
        }

        public Vector3 GetTangentRaw(float t)
        {
            int segCount = SegmentCount;
            if (segCount < 1) return Vector3.forward;
            t = Mathf.Clamp01(t);
            float scaledT = t * segCount;
            int segIndex = Mathf.Min(Mathf.FloorToInt(scaledT), segCount - 1);
            float localT = scaledT - segIndex;

            int i = segIndex * 3;
            Vector3 p0 = _controlPoints[i].position;
            Vector3 p1 = _controlPoints[i + 1].position;
            Vector3 p2 = _controlPoints[i + 2].position;
            Vector3 p3 = _controlPoints[i + 3].position;

            float u = 1f - localT;
            Vector3 tangent = 3f * u * u * (p1 - p0) + 6f * u * localT * (p2 - p1) + 3f * localT * localT * (p3 - p2);
            return tangent.sqrMagnitude > Mathf.Epsilon ? tangent.normalized : Vector3.forward;
        }

        public Vector3 GetPoint(float t)
        {
            return GetPointRaw(t);
        }

        public Vector3 GetTangent(float t)
        {
            return GetTangentRaw(t);
        }

        public Vector3 GetNormal(float t)
        {
            Vector3 tangent = GetTangent(t);
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(tangent, up)) > 0.999f)
                up = Vector3.right;
            Vector3 normal = Vector3.Cross(tangent, up);
            return normal.sqrMagnitude > Mathf.Epsilon ? normal.normalized : Vector3.up;
        }

        public Vector3 GetBinormal(float t)
        {
            Vector3 tangent = GetTangent(t);
            Vector3 normal = GetNormal(t);
            return Vector3.Cross(tangent, normal).normalized;
        }

        public float GetDistanceFromParameter(float t)
        {
            EnsureArcLengthTable();
            if (_arcLengthTable.distances.Count < 2) return 0f;
            t = Mathf.Clamp01(t);

            for (int i = 1; i < _arcLengthTable.parameters.Count; i++)
            {
                if (_arcLengthTable.parameters[i] >= t)
                {
                    float prevParam = _arcLengthTable.parameters[i - 1];
                    float currParam = _arcLengthTable.parameters[i];
                    float prevDist = _arcLengthTable.distances[i - 1];
                    float currDist = _arcLengthTable.distances[i];
                    float ratio = (currParam - prevParam) > Mathf.Epsilon ? (t - prevParam) / (currParam - prevParam) : 0f;
                    return Mathf.Lerp(prevDist, currDist, ratio);
                }
            }
            return _arcLengthTable.totalLength;
        }

        public float GetParameterFromDistance(float distance)
        {
            EnsureArcLengthTable();
            if (_arcLengthTable.distances.Count < 2) return 0f;
            if (distance <= 0f) return 0f;
            if (distance >= _arcLengthTable.totalLength) return 1f;

            int lo = 0;
            int hi = _arcLengthTable.distances.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (_arcLengthTable.distances[mid] < distance)
                    lo = mid;
                else
                    hi = mid;
            }

            float prevDist = _arcLengthTable.distances[lo];
            float currDist = _arcLengthTable.distances[hi];
            float prevParam = _arcLengthTable.parameters[lo];
            float currParam = _arcLengthTable.parameters[hi];
            float ratio = (currDist - prevDist) > Mathf.Epsilon ? (distance - prevDist) / (currDist - prevDist) : 0f;
            return Mathf.Lerp(prevParam, currParam, ratio);
        }

        public Vector3 GetNearestPoint(Vector3 worldPos)
        {
            EnsureArcLengthTable();
            if (_arcLengthTable.distances.Count < 2) return _controlPoints.Count > 0 ? _controlPoints[0].position : Vector3.zero;

            float minDistSq = float.MaxValue;
            float bestT = 0f;
            Vector3 nearest = Vector3.zero;

            int steps = Mathf.Max(100, SegmentCount * 20);
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector3 pt = GetPointRaw(t);
                float distSq = (worldPos - pt).sqrMagnitude;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    bestT = t;
                    nearest = pt;
                }
            }

            float refineStep = 1f / steps;
            for (int iter = 0; iter < 5; iter++)
            {
                float tLo = Mathf.Max(0f, bestT - refineStep);
                float tHi = Mathf.Min(1f, bestT + refineStep);
                refineStep *= 0.1f;
                for (int s = 0; s <= 20; s++)
                {
                    float t = Mathf.Lerp(tLo, tHi, (float)s / 20f);
                    Vector3 pt = GetPointRaw(t);
                    float distSq = (worldPos - pt).sqrMagnitude;
                    if (distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        bestT = t;
                        nearest = pt;
                    }
                }
            }

            return nearest;
        }

        public float GetCurvatureAt(float t)
        {
            int segCount = SegmentCount;
            if (segCount < 1) return 0f;
            t = Mathf.Clamp01(t);
            float scaledT = t * segCount;
            int segIndex = Mathf.Min(Mathf.FloorToInt(scaledT), segCount - 1);
            float localT = scaledT - segIndex;

            int i = segIndex * 3;
            Vector3 p0 = _controlPoints[i].position;
            Vector3 p1 = _controlPoints[i + 1].position;
            Vector3 p2 = _controlPoints[i + 2].position;
            Vector3 p3 = _controlPoints[i + 3].position;

            float u = 1f - localT;
            Vector3 firstDeriv = 3f * u * u * (p1 - p0) + 6f * u * localT * (p2 - p1) + 3f * localT * localT * (p3 - p2);
            Vector3 secondDeriv = 6f * u * (p2 - 2f * p1 + p0) + 6f * localT * (p3 - 2f * p2 + p1);

            float d1Mag = firstDeriv.magnitude;
            if (d1Mag < Mathf.Epsilon) return 0f;

            Vector3 cross = Vector3.Cross(firstDeriv, secondDeriv);
            return cross.magnitude / (d1Mag * d1Mag * d1Mag);
        }
    }
}
