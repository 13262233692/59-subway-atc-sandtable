using UnityEngine;
using CBTC.Sandtable.Track;

namespace CBTC.Sandtable.Train
{
    public enum TrainMovementState
    {
        Stopped,
        Accelerating,
        Cruising,
        Coasting,
        Braking,
        EmergencyBraking
    }

    public class SplineTrainController : MonoBehaviour
    {
        [SerializeField] private TrainPhysicsConfig _config;
        [SerializeField] private TrackNetwork _network;

        private TrackSegment _currentSegment;
        private float _currentDistance;
        private float _currentSpeed;
        private float _currentAcceleration;
        private TrainMovementState _currentState;
        private bool _initialized;

        public TrainMovementState CurrentState => _currentState;
        public float CurrentSpeed => _currentSpeed;
        public float CurrentDistance => _currentDistance;
        public string CurrentSegmentId => _currentSegment != null ? _currentSegment.SegmentId : string.Empty;
        public TrackSegment CurrentSegment => _currentSegment;

        public void Configure(TrainPhysicsConfig config, TrackNetwork network)
        {
            _config = config;
            _network = network;
        }

        public void Initialize(TrackSegment segment, float startDistance, float startSpeed)
        {
            _currentSegment = segment;
            _currentDistance = Mathf.Clamp(startDistance, 0f, segment != null ? segment.Length : 0f);
            _currentSpeed = Mathf.Clamp(startSpeed, 0f, _config != null ? _config.maxSpeed : 0f);
            _currentAcceleration = 0f;
            _currentState = _currentSpeed < 0.01f ? TrainMovementState.Stopped : TrainMovementState.Cruising;
            _initialized = true;
            UpdateTransform();
        }

        public void UpdateMovement(float deltaTime, float targetSpeed)
        {
            if (!_initialized || _currentSegment == null || _config == null || deltaTime <= 0f) return;

            float speedDiff = targetSpeed - _currentSpeed;

            if (_currentState == TrainMovementState.EmergencyBraking)
            {
                _currentAcceleration = -_config.emergencyDeceleration;
            }
            else if (speedDiff > 0.01f)
            {
                float maxTractionForce = _config.tractionEffort;
                float maxTractionAccel = Mathf.Min(maxTractionForce / _config.mass, _config.maxAcceleration);
                float adhesionLimit = _config.wheelAdhesionCoeff * Physics.gravity.magnitude;
                _currentAcceleration = Mathf.Min(maxTractionAccel, adhesionLimit);
                _currentState = TrainMovementState.Accelerating;
            }
            else if (speedDiff < -0.01f)
            {
                float maxBrakingForce = _config.brakingEffort;
                float maxBrakingAccel = Mathf.Min(maxBrakingForce / _config.mass, _config.maxDeceleration);
                float adhesionLimit = _config.wheelAdhesionCoeff * Physics.gravity.magnitude;
                _currentAcceleration = -Mathf.Min(maxBrakingAccel, adhesionLimit);
                _currentState = TrainMovementState.Braking;
            }
            else
            {
                _currentAcceleration = 0f;
                _currentState = _currentSpeed < 0.01f ? TrainMovementState.Stopped : TrainMovementState.Cruising;
            }

            float rollingResist = _config.CalculateRollingResistance(_currentSpeed) / _config.mass;
            float aeroDrag = _config.CalculateAerodynamicDrag(_currentSpeed) / _config.mass;
            float resistanceAccel = rollingResist + aeroDrag;

            if (_currentSpeed > 0.01f)
            {
                _currentAcceleration -= resistanceAccel;
            }

            _currentSpeed += _currentAcceleration * deltaTime;
            _currentSpeed = Mathf.Clamp(_currentSpeed, 0f, _config.maxSpeed);

            float curvatureRadius = _currentSegment.CurveRadius;
            if (curvatureRadius > 0f)
            {
                float curveLimit = _config.CalculateMaxSpeedAtCurvature(curvatureRadius);
                if (_currentSpeed > curveLimit)
                {
                    _currentSpeed = curveLimit;
                }
            }

            float segmentSpeedLimit = _currentSegment.GetSpeedLimitAtDistance(_currentDistance);
            if (_currentSpeed > segmentSpeedLimit)
            {
                _currentSpeed = segmentSpeedLimit;
            }

            float deltaDist = _currentSpeed * deltaTime + 0.5f * _currentAcceleration * deltaTime * deltaTime;
            if (deltaDist < 0f) deltaDist = 0f;

            _currentDistance += deltaDist;

            HandleSegmentTransition();
            UpdateTransform();
        }

        private void HandleSegmentTransition()
        {
            if (_currentSegment == null) return;

            while (_currentDistance >= _currentSegment.Length)
            {
                float overflow = _currentDistance - _currentSegment.Length;
                TrackSegment nextSegment = FindNextSegment();

                if (nextSegment != null)
                {
                    _currentSegment = nextSegment;
                    _currentDistance = overflow;
                }
                else
                {
                    _currentDistance = _currentSegment.Length;
                    _currentSpeed = 0f;
                    _currentAcceleration = 0f;
                    _currentState = TrainMovementState.Stopped;
                    return;
                }
            }

            while (_currentDistance < 0f)
            {
                TrackSegment prevSegment = FindPreviousSegment();
                if (prevSegment != null)
                {
                    _currentDistance += prevSegment.Length;
                    _currentSegment = prevSegment;
                }
                else
                {
                    _currentDistance = 0f;
                    _currentSpeed = 0f;
                    _currentAcceleration = 0f;
                    _currentState = TrainMovementState.Stopped;
                    return;
                }
            }
        }

        private TrackSegment FindNextSegment()
        {
            if (_network == null || _currentSegment == null) return null;

            string endNodeId = _currentSegment.EndStation;
            TrackNode endNode = _network.GetNode(endNodeId);
            if (endNode == null) return null;

            for (int i = 0; i < endNode.Connections.Count; i++)
            {
                string segId = endNode.Connections[i].segmentId;
                if (segId == _currentSegment.SegmentId) continue;
                TrackSegment seg = _network.GetSegment(segId);
                if (seg != null) return seg;
            }

            return null;
        }

        private TrackSegment FindPreviousSegment()
        {
            if (_network == null || _currentSegment == null) return null;

            string startNodeId = _currentSegment.StartStation;
            TrackNode startNode = _network.GetNode(startNodeId);
            if (startNode == null) return null;

            for (int i = 0; i < startNode.Connections.Count; i++)
            {
                string segId = startNode.Connections[i].segmentId;
                if (segId == _currentSegment.SegmentId) continue;
                TrackSegment seg = _network.GetSegment(segId);
                if (seg != null) return seg;
            }

            return null;
        }

        private void UpdateTransform()
        {
            if (_currentSegment == null) return;

            Vector3 pos = _currentSegment.GetWorldPosition(_currentDistance);
            Vector3 tangent = _currentSegment.GetWorldTangent(_currentDistance);
            Vector3 normal = _currentSegment.GetWorldNormal(_currentDistance);

            transform.position = pos;
            if (tangent.sqrMagnitude > Mathf.Epsilon)
            {
                transform.rotation = Quaternion.LookRotation(tangent, normal);
            }
        }

        public Vector3 GetHeadPosition()
        {
            if (_currentSegment == null || _config == null) return transform.position;

            float halfLength = _config.length * 0.5f;
            float headDist = _currentDistance + halfLength;

            if (headDist <= _currentSegment.Length)
            {
                return _currentSegment.GetWorldPosition(headDist);
            }

            return _currentSegment.GetWorldPosition(_currentSegment.Length);
        }

        public Vector3 GetTailPosition()
        {
            if (_currentSegment == null || _config == null) return transform.position;

            float halfLength = _config.length * 0.5f;
            float tailDist = _currentDistance - halfLength;

            if (tailDist >= 0f)
            {
                return _currentSegment.GetWorldPosition(tailDist);
            }

            return _currentSegment.GetWorldPosition(0f);
        }

        public float GetDistanceToSegmentEnd()
        {
            if (_currentSegment == null) return 0f;
            return _currentSegment.Length - _currentDistance;
        }

        public void ApplyEmergencyBrake()
        {
            _currentState = TrainMovementState.EmergencyBraking;
        }

        public Vector3 GetPredictedPosition(float deltaTimeAhead)
        {
            if (_currentSegment == null) return transform.position;

            float predAccel = _currentAcceleration;
            float predSpeed = _currentSpeed + predAccel * deltaTimeAhead;
            predSpeed = Mathf.Clamp(predSpeed, 0f, _config != null ? _config.maxSpeed : predSpeed);
            float predDist = _currentSpeed * deltaTimeAhead + 0.5f * predAccel * deltaTimeAhead * deltaTimeAhead;
            if (predDist < 0f) predDist = 0f;

            float targetDist = _currentDistance + predDist;

            if (targetDist <= _currentSegment.Length)
            {
                return _currentSegment.GetWorldPosition(targetDist);
            }

            float overflow = targetDist - _currentSegment.Length;
            TrackSegment nextSeg = FindNextSegment();
            if (nextSeg != null && overflow <= nextSeg.Length)
            {
                return nextSeg.GetWorldPosition(overflow);
            }

            return _currentSegment.GetWorldPosition(_currentSegment.Length);
        }
    }
}
