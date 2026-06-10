using System.Collections.Generic;
using UnityEngine;
using CBTC.Sandtable.Signal;

namespace CBTC.Sandtable.Visualization
{
    public class BrakingCurveVisualizer : MonoBehaviour
    {
        [SerializeField] private ATOController _atoController;
        [SerializeField] private float _displayHeight = 5f;
        [SerializeField] private float _displayScale = 0.01f;
        [SerializeField] private bool _showBrakingCurve = true;
        [SerializeField] private bool _showMovementAuthority = true;
        [SerializeField] private bool _showSpeedRestrictions = true;
        [SerializeField] private float _movementAuthorityDistance = 1000f;
        [SerializeField] private float _frontTrainDistance = 800f;
        [SerializeField] private float _frontTrainLength = 22f;

        private List<BrakingPoint> _brakingCurvePoints = new List<BrakingPoint>();

        public float DisplayHeight { get => _displayHeight; set => _displayHeight = value; }
        public float DisplayScale { get => _displayScale; set => _displayScale = value; }
        public bool ShowBrakingCurve { get => _showBrakingCurve; set => _showBrakingCurve = value; }
        public bool ShowMovementAuthority { get => _showMovementAuthority; set => _showMovementAuthority = value; }
        public bool ShowSpeedRestrictions { get => _showSpeedRestrictions; set => _showSpeedRestrictions = value; }

        private void OnDrawGizmos()
        {
            if (_atoController == null) return;

            UpdateBrakingCurveData();

            Vector3 origin = transform.position;
            Vector3 forward = transform.forward;

            if (_showSpeedRestrictions)
            {
                DrawSpeedRestrictions(origin, forward);
            }

            if (_showBrakingCurve)
            {
                DrawBrakingCurve(origin, forward);
            }

            if (_showMovementAuthority)
            {
                DrawMovementAuthority(origin, forward);
            }

            DrawFrontTrain(origin, forward);
            DrawAxes(origin, forward);
        }

        private void UpdateBrakingCurveData()
        {
            _brakingCurvePoints = _atoController.GetBrakingCurvePoints();
        }

        private void DrawBrakingCurve(Vector3 origin, Vector3 forward)
        {
            if (_brakingCurvePoints == null || _brakingCurvePoints.Count < 2) return;

            Gizmos.color = Color.red;

            for (int i = 0; i < _brakingCurvePoints.Count - 1; i++)
            {
                BrakingPoint p0 = _brakingCurvePoints[i];
                BrakingPoint p1 = _brakingCurvePoints[i + 1];

                Vector3 start = origin + forward * p0.distance * _displayScale + Vector3.up * (p0.speed * _displayScale * 3.6f + _displayHeight);
                Vector3 end = origin + forward * p1.distance * _displayScale + Vector3.up * (p1.speed * _displayScale * 3.6f + _displayHeight);

                Gizmos.DrawLine(start, end);
            }
        }

        private void DrawMovementAuthority(Vector3 origin, Vector3 forward)
        {
            Gizmos.color = Color.magenta;

            Vector3 basePos = origin + forward * _movementAuthorityDistance * _displayScale;
            Vector3 top = basePos + Vector3.up * (_displayHeight + 30f * _displayScale);

            Gizmos.DrawLine(basePos, top);

            float arrowSize = 1f;
            Vector3 arrowLeft = top + Vector3.left * arrowSize + Vector3.down * arrowSize;
            Vector3 arrowRight = top + Vector3.right * arrowSize + Vector3.down * arrowSize;
            Gizmos.DrawLine(top, arrowLeft);
            Gizmos.DrawLine(top, arrowRight);

            Gizmos.DrawLine(basePos + Vector3.left * arrowSize, basePos + Vector3.right * arrowSize);
        }

        private void DrawSpeedRestrictions(Vector3 origin, Vector3 forward)
        {
            if (_atoController.RestrictionProfile == null) return;

            List<SpeedRestriction> restrictions = _atoController.RestrictionProfile.Restrictions;
            if (restrictions == null || restrictions.Count == 0) return;

            Gizmos.color = Color.yellow;

            for (int i = 0; i < restrictions.Count; i++)
            {
                SpeedRestriction sr = restrictions[i];

                float startDist = sr.startDistance * _displayScale;
                float endDist = sr.endDistance * _displayScale;
                float speedHeight = sr.speedLimit * _displayScale * 3.6f + _displayHeight;

                Vector3 startBottom = origin + forward * startDist + Vector3.up * _displayHeight;
                Vector3 startTop = origin + forward * startDist + Vector3.up * speedHeight;
                Vector3 endTop = origin + forward * endDist + Vector3.up * speedHeight;
                Vector3 endBottom = origin + forward * endDist + Vector3.up * _displayHeight;

                Gizmos.DrawLine(startBottom, startTop);
                Gizmos.DrawLine(startTop, endTop);
                Gizmos.DrawLine(endTop, endBottom);
            }
        }

        private void DrawFrontTrain(Vector3 origin, Vector3 forward)
        {
            Gizmos.color = Color.red;

            float frontStart = (_frontTrainDistance - _frontTrainLength * 0.5f) * _displayScale;
            float frontEnd = (_frontTrainDistance + _frontTrainLength * 0.5f) * _displayScale;
            float height = _displayHeight + 1f;
            float width = 1f;

            Vector3 center = origin + forward * ((frontStart + frontEnd) * 0.5f) + Vector3.up * height;
            Vector3 size = new Vector3(width, height * 0.3f, (frontEnd - frontStart));

            Gizmos.DrawCube(center, size);
        }

        private void DrawAxes(Vector3 origin, Vector3 forward)
        {
            Gizmos.color = Color.white;
            Vector3 axisEnd = origin + forward * 50f + Vector3.up * _displayHeight;
            Gizmos.DrawLine(origin + Vector3.up * _displayHeight, axisEnd);

            Gizmos.color = Color.gray;
            Vector3 vertEnd = origin + Vector3.up * (_displayHeight + 20f * _displayScale);
            Gizmos.DrawLine(origin + Vector3.up * _displayHeight, vertEnd);
        }
    }
}
