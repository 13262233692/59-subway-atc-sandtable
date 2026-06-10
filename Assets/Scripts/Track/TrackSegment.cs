using System;
using System.Collections.Generic;
using UnityEngine;

namespace CBTC.Sandtable.Track
{
    public enum SectionType
    {
        Mainline,
        Station,
        Depot,
        Turnout
    }

    [Serializable]
    public struct TrackSection
    {
        public float startDistance;
        public float endDistance;
        public float speedLimit;
        public SectionType sectionType;

        public float Length => endDistance - startDistance;

        public bool ContainsDistance(float distance)
        {
            return distance >= startDistance && distance <= endDistance;
        }
    }

    public class TrackSegment : MonoBehaviour
    {
        [SerializeField] private string _segmentId;
        [SerializeField] private string _lineId;
        [SerializeField] private string _startStation;
        [SerializeField] private string _endStation;
        [SerializeField] private float _length;
        [SerializeField] private float _speedLimit;
        [SerializeField] private float _grade;
        [SerializeField] private float _curveRadius;
        [SerializeField] private CubicBezierSpline _spline;
        [SerializeField] private List<TrackSection> _sections = new List<TrackSection>();

        public string SegmentId => _segmentId;
        public string LineId => _lineId;
        public string StartStation => _startStation;
        public string EndStation => _endStation;
        public float Length => _length;
        public float SpeedLimit => _speedLimit;
        public float Grade => _grade;
        public float CurveRadius => _curveRadius;
        public CubicBezierSpline Spline => _spline;
        public List<TrackSection> Sections => _sections;
        public int SectionCount => _sections.Count;

        public void Initialize(string segmentId, string lineId, CubicBezierSpline spline)
        {
            _segmentId = segmentId;
            _lineId = lineId;
            _spline = spline;
            if (_spline != null)
            {
                _spline.Recalculate();
                _length = _spline.TotalLength;
            }
        }

        public void SetEndpoints(string startStation, string endStation)
        {
            _startStation = startStation;
            _endStation = endStation;
        }

        public void SetProperties(float speedLimit, float grade, float curveRadius)
        {
            _speedLimit = speedLimit;
            _grade = grade;
            _curveRadius = curveRadius;
        }

        public void AddSection(TrackSection section)
        {
            _sections.Add(section);
        }

        public void ClearSections()
        {
            _sections.Clear();
        }

        private bool TryGetSectionAtDistance(float distance, out TrackSection section)
        {
            for (int i = 0; i < _sections.Count; i++)
            {
                if (_sections[i].ContainsDistance(distance))
                {
                    section = _sections[i];
                    return true;
                }
            }
            section = default;
            return false;
        }

        public TrackSection GetSectionAtDistance(float distance)
        {
            TryGetSectionAtDistance(distance, out TrackSection section);
            return section;
        }

        public SectionType GetSectionTypeAtDistance(float distance)
        {
            if (TryGetSectionAtDistance(distance, out TrackSection section))
                return section.sectionType;
            return SectionType.Mainline;
        }

        public float GetSpeedLimitAtDistance(float distance)
        {
            if (TryGetSectionAtDistance(distance, out TrackSection section))
                return section.speedLimit;
            return _speedLimit;
        }

        public Vector3 GetWorldPosition(float distance)
        {
            if (_spline == null) return transform.position;
            float t = _spline.GetParameterFromDistance(distance);
            return _spline.GetPoint(t);
        }

        public Vector3 GetWorldTangent(float distance)
        {
            if (_spline == null) return transform.forward;
            float t = _spline.GetParameterFromDistance(distance);
            return _spline.GetTangent(t);
        }

        public Vector3 GetWorldNormal(float distance)
        {
            if (_spline == null) return transform.up;
            float t = _spline.GetParameterFromDistance(distance);
            return _spline.GetNormal(t);
        }

        public Vector3 GetWorldBinormal(float distance)
        {
            if (_spline == null) return transform.right;
            float t = _spline.GetParameterFromDistance(distance);
            return _spline.GetBinormal(t);
        }

        public Quaternion GetWorldOrientation(float distance)
        {
            Vector3 tangent = GetWorldTangent(distance);
            Vector3 normal = GetWorldNormal(distance);
            return Quaternion.LookRotation(tangent, normal);
        }

        public float GetCurvatureAtDistance(float distance)
        {
            if (_spline == null) return 0f;
            float t = _spline.GetParameterFromDistance(distance);
            return _spline.GetCurvatureAt(t);
        }

        public float GetGradeAtDistance(float distance)
        {
            Vector3 tangent = GetWorldTangent(distance);
            return Mathf.Asin(tangent.y) * Mathf.Rad2Deg;
        }
    }
}
