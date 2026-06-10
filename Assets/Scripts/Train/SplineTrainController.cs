using System.Collections.Generic;
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

    public class TrainCarriage
    {
        public Transform transform;
        public TrackSegment segment;
        public float distance;
        public Quaternion rotation;
        public Vector3 position;

        public TrainCarriage(Transform t, TrackSegment seg, float dist)
        {
            transform = t;
            segment = seg;
            distance = dist;
            if (seg != null)
            {
                position = seg.GetWorldPosition(dist);
                Vector3 tan = seg.GetWorldTangent(dist);
                Vector3 nor = seg.GetWorldNormal(dist);
                if (tan.sqrMagnitude > Mathf.Epsilon)
                    rotation = Quaternion.LookRotation(tan, nor);
                else
                    rotation = Quaternion.identity;
            }
            else
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
            }
        }
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

        private List<TrainCarriage> _carriages = new List<TrainCarriage>();
        private float _carriageSpacing = 22f;
        private int _carriageCount = 1;
        private float _maxDistancePerSubstep = 2f;
        private float _rotationSlerpFactor = 12f;

        private TrackSegment _prevSegment;
        private float _prevDistance;
        private Vector3 _prevTangent;

        public TrainMovementState CurrentState => _currentState;
        public float CurrentSpeed => _currentSpeed;
        public float CurrentDistance => _currentDistance;
        public string CurrentSegmentId => _currentSegment != null ? _currentSegment.SegmentId : string.Empty;
        public TrackSegment CurrentSegment => _currentSegment;
        public List<TrainCarriage> Carriages => _carriages;
        public int CarriageCount => _carriageCount;

        public void Configure(TrainPhysicsConfig config, TrackNetwork network)
        {
            _config = config;
            _network = network;
        }

        public void SetCarriageCount(int count, float spacing)
        {
            _carriageCount = Mathf.Max(1, count);
            _carriageSpacing = spacing;
        }

        public void Initialize(TrackSegment segment, float startDistance, float startSpeed)
        {
            _currentSegment = segment;
            _currentDistance = Mathf.Clamp(startDistance, 0f, segment != null ? segment.Length : 0f);
            _currentSpeed = Mathf.Clamp(startSpeed, 0f, _config != null ? _config.maxSpeed : 0f);
            _currentAcceleration = 0f;
            _currentState = _currentSpeed < 0.01f ? TrainMovementState.Stopped : TrainMovementState.Cruising;

            _prevSegment = _currentSegment;
            _prevDistance = _currentDistance;
            if (_currentSegment != null)
                _prevTangent = _currentSegment.GetWorldTangent(_currentDistance);
            else
                _prevTangent = Vector3.forward;

            BuildCarriages();
            UpdateAllTransformsImmediate();

            _initialized = true;
        }

        private void BuildCarriages()
        {
            _carriages.Clear();
            for (int i = 0; i < _carriageCount; i++)
            {
                GameObject carriageObj;
                if (i == 0)
                {
                    carriageObj = gameObject;
                }
                else
                {
                    carriageObj = new GameObject($"Carriage_{i}");
                    carriageObj.transform.SetParent(transform.parent);
                }

                float carriageDist = _currentDistance - i * _carriageSpacing;
                TrackSegment carriageSeg = _currentSegment;
                if (carriageDist < 0f && _network != null)
                {
                    carriageSeg = ResolveBackwardSegment(_currentSegment, -carriageDist, out carriageDist);
                }

                TrainCarriage carriage = new TrainCarriage(carriageObj.transform, carriageSeg, Mathf.Max(0f, carriageDist));
                _carriages.Add(carriage);
            }
        }

        private TrackSegment ResolveBackwardSegment(TrackSegment from, float backDist, out float remaining)
        {
            remaining = backDist;
            TrackSegment seg = from;
            while (remaining > 0f && seg != null)
            {
                if (remaining <= seg.Length)
                {
                    remaining = seg.Length - remaining;
                    return seg;
                }
                remaining -= seg.Length;
                seg = FindPreviousSegment(seg);
            }
            remaining = 0f;
            return seg ?? from;
        }

        public void FixedUpdateMovement(float fixedDeltaTime, float targetSpeed)
        {
            if (!_initialized || _currentSegment == null || _config == null || fixedDeltaTime <= 0f) return;

            _prevSegment = _currentSegment;
            _prevDistance = _currentDistance;
            _prevTangent = _currentSegment.GetWorldTangent(_currentDistance);

            float maxStepDist = _maxDistancePerSubstep;
            float totalDist = _currentSpeed * fixedDeltaTime + 0.5f * Mathf.Abs(_currentAcceleration) * fixedDeltaTime * fixedDeltaTime;
            int substeps = Mathf.Max(1, Mathf.CeilToInt(totalDist / maxStepDist));
            float subDt = fixedDeltaTime / substeps;

            for (int s = 0; s < substeps; s++)
            {
                ExecuteSubstep(subDt, targetSpeed);
            }

            UpdateAllCarriages();
        }

        private void ExecuteSubstep(float dt, float targetSpeed)
        {
            float speedDiff = targetSpeed - _currentSpeed;

            if (_currentState == TrainMovementState.EmergencyBraking)
            {
                _currentAcceleration = -_config.emergencyDeceleration;
            }
            else if (speedDiff > 0.01f)
            {
                float maxTractionAccel = Mathf.Min(_config.tractionEffort / _config.mass, _config.maxAcceleration);
                float adhesionLimit = _config.wheelAdhesionCoeff * Physics.gravity.magnitude;
                _currentAcceleration = Mathf.Min(maxTractionAccel, adhesionLimit);
                _currentState = TrainMovementState.Accelerating;
            }
            else if (speedDiff < -0.01f)
            {
                float maxBrakingAccel = Mathf.Min(_config.brakingEffort / _config.mass, _config.maxDeceleration);
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
            if (_currentSpeed > 0.01f)
                _currentAcceleration -= (rollingResist + aeroDrag);

            _currentSpeed += _currentAcceleration * dt;
            _currentSpeed = Mathf.Clamp(_currentSpeed, 0f, _config.maxSpeed);

            ApplyCurvatureSpeedLimit();

            float segmentSpeedLimit = _currentSegment.GetSpeedLimitAtDistance(_currentDistance);
            if (_currentSpeed > segmentSpeedLimit)
                _currentSpeed = segmentSpeedLimit;

            float deltaDist = _currentSpeed * dt + 0.5f * _currentAcceleration * dt * dt;
            if (deltaDist < 0f) deltaDist = 0f;

            _currentDistance += deltaDist;
            HandleSegmentTransition();

            UpdateHeadTransformSlerp(dt);
        }

        private void ApplyCurvatureSpeedLimit()
        {
            if (_currentSegment == null || _config == null) return;

            float curvature = _currentSegment.GetCurvatureAtDistance(_currentDistance);
            if (curvature > 0.0001f)
            {
                float curvatureRadius = 1f / curvature;
                float curveLimit = _config.CalculateMaxSpeedAtCurvature(curvatureRadius);
                if (_currentSpeed > curveLimit)
                {
                    _currentSpeed = curveLimit;
                    if (_currentState == TrainMovementState.Accelerating || _currentState == TrainMovementState.Cruising)
                        _currentState = TrainMovementState.Braking;
                }
            }

            float lookAhead = _currentSpeed * 2f;
            if (lookAhead > 1f)
            {
                float probeDist = _currentDistance + lookAhead;
                TrackSegment probeSeg = _currentSegment;
                while (probeDist > probeSeg.Length && probeSeg != null)
                {
                    probeDist -= probeSeg.Length;
                    probeSeg = FindNextSegment(probeSeg);
                    if (probeSeg == null) break;
                }
                if (probeSeg != null && probeDist <= probeSeg.Length)
                {
                    float futureCurvature = probeSeg.GetCurvatureAtDistance(probeDist);
                    if (futureCurvature > 0.0001f)
                    {
                        float futureRadius = 1f / futureCurvature;
                        float futureLimit = _config.CalculateMaxSpeedAtCurvature(futureRadius);
                        float brakingDistNeeded = (_currentSpeed * _currentSpeed - futureLimit * futureLimit) / (2f * _config.maxDeceleration);
                        if (lookAhead < brakingDistNeeded && _currentSpeed > futureLimit)
                        {
                            _currentSpeed = Mathf.Max(futureLimit, _currentSpeed - _config.maxDeceleration * Time.fixedDeltaTime);
                        }
                    }
                }
            }
        }

        private void UpdateHeadTransformSlerp(float dt)
        {
            if (_currentSegment == null) return;

            Vector3 targetPos = _currentSegment.GetWorldPosition(_currentDistance);
            Vector3 tangent = _currentSegment.GetWorldTangent(_currentDistance);
            Vector3 normal = _currentSegment.GetWorldNormal(_currentDistance);

            transform.position = targetPos;

            if (tangent.sqrMagnitude > Mathf.Epsilon)
            {
                Quaternion targetRot = Quaternion.LookRotation(tangent, normal);
                float slerpT = 1f - Mathf.Exp(-_rotationSlerpFactor * dt);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, slerpT);

                Vector3 actualForward = transform.forward;
                float deviation = Vector3.Angle(actualForward, tangent);
                if (deviation > 5f)
                {
                    transform.rotation = Quaternion.LookRotation(tangent, normal);
                }
            }
        }

        private void UpdateAllCarriages()
        {
            if (_carriages.Count == 0) return;

            TrainCarriage head = _carriages[0];
            head.segment = _currentSegment;
            head.distance = _currentDistance;
            if (_currentSegment != null)
            {
                head.position = _currentSegment.GetWorldPosition(_currentDistance);
                Vector3 tan = _currentSegment.GetWorldTangent(_currentDistance);
                Vector3 nor = _currentSegment.GetWorldNormal(_currentDistance);
                if (tan.sqrMagnitude > Mathf.Epsilon)
                    head.rotation = Quaternion.LookRotation(tan, nor);
            }

            for (int i = 1; i < _carriages.Count; i++)
            {
                TrainCarriage prev = _carriages[i - 1];
                TrainCarriage curr = _carriages[i];

                float targetDist = prev.distance - _carriageSpacing;

                TrackSegment seg = prev.segment;
                while (targetDist < 0f && seg != null)
                {
                    TrackSegment prevSeg = FindPreviousSegment(seg);
                    if (prevSeg != null)
                    {
                        targetDist += prevSeg.Length;
                        seg = prevSeg;
                    }
                    else
                    {
                        targetDist = 0f;
                        break;
                    }
                }

                curr.segment = seg;
                curr.distance = Mathf.Max(0f, targetDist);

                if (seg != null)
                {
                    curr.position = seg.GetWorldPosition(curr.distance);
                    Vector3 tan = seg.GetWorldTangent(curr.distance);
                    Vector3 nor = seg.GetWorldNormal(curr.distance);
                    if (tan.sqrMagnitude > Mathf.Epsilon)
                        curr.rotation = Quaternion.LookRotation(tan, nor);
                }

                if (curr.transform != null)
                {
                    curr.transform.position = curr.position;

                    if (i == 0)
                    {
                        float slerpT = 1f - Mathf.Exp(-_rotationSlerpFactor * Time.fixedDeltaTime);
                        curr.transform.rotation = Quaternion.Slerp(curr.transform.rotation, curr.rotation, slerpT);
                    }
                    else
                    {
                        float slerpT = 1f - Mathf.Exp(-_rotationSlerpFactor * Time.fixedDeltaTime);
                        curr.transform.rotation = Quaternion.Slerp(curr.transform.rotation, curr.rotation, slerpT);

                        Vector3 actualForward = curr.transform.forward;
                        Vector3 desiredForward = curr.rotation * Vector3.forward;
                        float deviation = Vector3.Angle(actualForward, desiredForward);
                        if (deviation > 5f)
                        {
                            curr.transform.rotation = curr.rotation;
                        }
                    }
                }
            }
        }

        private void UpdateAllTransformsImmediate()
        {
            if (_currentSegment == null) return;

            transform.position = _currentSegment.GetWorldPosition(_currentDistance);
            Vector3 tangent = _currentSegment.GetWorldTangent(_currentDistance);
            Vector3 normal = _currentSegment.GetWorldNormal(_currentDistance);
            if (tangent.sqrMagnitude > Mathf.Epsilon)
                transform.rotation = Quaternion.LookRotation(tangent, normal);

            for (int i = 0; i < _carriages.Count; i++)
            {
                TrainCarriage c = _carriages[i];
                if (c.transform != null && c.segment != null)
                {
                    c.transform.position = c.position;
                    c.transform.rotation = c.rotation;
                }
            }
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
            return FindNextSegment(_currentSegment);
        }

        private TrackSegment FindNextSegment(TrackSegment from)
        {
            if (_network == null || from == null) return null;

            string endNodeId = from.EndStation;
            TrackNode endNode = _network.GetNode(endNodeId);
            if (endNode == null) return null;

            for (int i = 0; i < endNode.Connections.Count; i++)
            {
                string segId = endNode.Connections[i].segmentId;
                if (segId == from.SegmentId) continue;
                TrackSegment seg = _network.GetSegment(segId);
                if (seg != null) return seg;
            }

            return null;
        }

        private TrackSegment FindPreviousSegment()
        {
            return FindPreviousSegment(_currentSegment);
        }

        private TrackSegment FindPreviousSegment(TrackSegment from)
        {
            if (_network == null || from == null) return null;

            string startNodeId = from.StartStation;
            TrackNode startNode = _network.GetNode(startNodeId);
            if (startNode == null) return null;

            for (int i = 0; i < startNode.Connections.Count; i++)
            {
                string segId = startNode.Connections[i].segmentId;
                if (segId == from.SegmentId) continue;
                TrackSegment seg = _network.GetSegment(segId);
                if (seg != null) return seg;
            }

            return null;
        }

        public Vector3 GetHeadPosition()
        {
            if (_currentSegment == null || _config == null) return transform.position;
            float halfLength = _config.length * 0.5f;
            float headDist = _currentDistance + halfLength;
            if (headDist <= _currentSegment.Length)
                return _currentSegment.GetWorldPosition(headDist);
            return _currentSegment.GetWorldPosition(_currentSegment.Length);
        }

        public Vector3 GetTailPosition()
        {
            if (_currentSegment == null || _config == null) return transform.position;
            float halfLength = _config.length * 0.5f;
            float tailDist = _currentDistance - halfLength;
            if (tailDist >= 0f)
                return _currentSegment.GetWorldPosition(tailDist);
            return _currentSegment.GetWorldPosition(0f);
        }

        public Vector3 GetCarriagePosition(int index)
        {
            if (index < 0 || index >= _carriages.Count) return transform.position;
            return _carriages[index].position;
        }

        public Quaternion GetCarriageRotation(int index)
        {
            if (index < 0 || index >= _carriages.Count) return transform.rotation;
            return _carriages[index].rotation;
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
                return _currentSegment.GetWorldPosition(targetDist);

            float overflow = targetDist - _currentSegment.Length;
            TrackSegment nextSeg = FindNextSegment();
            if (nextSeg != null && overflow <= nextSeg.Length)
                return nextSeg.GetWorldPosition(overflow);

            return _currentSegment.GetWorldPosition(_currentSegment.Length);
        }

        public void UpdateMovement(float deltaTime, float targetSpeed)
        {
            FixedUpdateMovement(deltaTime, targetSpeed);
        }
    }
}
