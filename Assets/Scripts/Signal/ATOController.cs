using System;
using System.Collections.Generic;
using UnityEngine;
using CBTC.Sandtable.Train;

namespace CBTC.Sandtable.Signal
{
    public enum ATOMode
    {
        Manual,
        Automatic,
        Supervised
    }

    public enum ATOState
    {
        Idle,
        Accelerating,
        Cruising,
        Coasting,
        TargetBraking,
        StationStop,
        DoorOperation
    }

    public class ATOController : MonoBehaviour
    {
        [SerializeField] private SplineTrainController _trainController;
        [SerializeField] private TrainPhysicsConfig _physicsConfig;
        [SerializeField] private float _reactionTime = 2.0f;

        private ATOMode _currentMode = ATOMode.Manual;
        private ATOState _currentState = ATOState.Idle;
        private float _targetSpeed;
        private float _stationStopPosition;
        private bool _hasStationStop;
        private List<BrakingPoint> _currentBrakingCurve = new List<BrakingPoint>();
        private SpeedRestrictionProfile _restrictionProfile = new SpeedRestrictionProfile();

        private const float StationStopTolerance = 0.3f;
        private const float FineBrakingDistance = 15f;
        private const float CoastingSpeedThreshold = 1.0f;

        public ATOMode CurrentMode => _currentMode;
        public ATOState CurrentState => _currentState;
        public float TargetSpeed => _targetSpeed;
        public SpeedRestrictionProfile RestrictionProfile => _restrictionProfile;

        public event Action OnStationArrived;
        public event Action OnSpeedExceeded;
        public event Action OnEmergencyBrakeApplied;

        public void Configure(SplineTrainController trainController, TrainPhysicsConfig physicsConfig)
        {
            _trainController = trainController;
            _physicsConfig = physicsConfig;
        }

        public void SetMode(ATOMode mode)
        {
            _currentMode = mode;
            if (mode == ATOMode.Manual)
            {
                _currentState = ATOState.Idle;
            }
        }

        public void SetTargetSpeed(float speed)
        {
            _targetSpeed = Mathf.Clamp(speed, 0f, _physicsConfig != null ? _physicsConfig.maxSpeed : speed);
        }

        public void SetStationStop(float distance)
        {
            _stationStopPosition = distance;
            _hasStationStop = true;
        }

        public void UpdateATO(float deltaTime)
        {
            float targetSpeed = ComputeTargetSpeed(deltaTime);
        }

        public float ComputeTargetSpeed(float deltaTime)
        {
            if (_trainController == null || _physicsConfig == null || deltaTime <= 0f)
                return _trainController != null ? _trainController.CurrentSpeed : 0f;

            if (_currentMode == ATOMode.Supervised)
            {
                SuperviseSpeed();
                return _trainController.CurrentSpeed;
            }

            if (_currentMode != ATOMode.Automatic)
                return _trainController.CurrentSpeed;

            float currentSpeed = _trainController.CurrentSpeed;
            float currentDistance = _trainController.CurrentDistance;

            float effectiveTargetSpeed = CalculateEffectiveTargetSpeed(currentSpeed, currentDistance);

            UpdateBrakingCurve(currentSpeed, currentDistance);

            if (IsTouchingBrakingCurve(currentSpeed, currentDistance))
            {
                float curveSpeed = BrakingCurve.GetTargetSpeedAtDistance(currentDistance, _currentBrakingCurve);
                effectiveTargetSpeed = Mathf.Min(effectiveTargetSpeed, curveSpeed);
            }

            if (_hasStationStop)
            {
                float distanceToStop = _stationStopPosition - currentDistance;
                if (distanceToStop > 0f)
                {
                    float stationBrakeSpeed = CalculateStationBrakingProfile(currentSpeed, distanceToStop);
                    effectiveTargetSpeed = Mathf.Min(effectiveTargetSpeed, stationBrakeSpeed);
                }
                else if (distanceToStop <= 0f && distanceToStop > -StationStopTolerance)
                {
                    effectiveTargetSpeed = 0f;
                    _currentState = ATOState.StationStop;

                    if (currentSpeed < 0.01f)
                    {
                        _currentState = ATOState.DoorOperation;
                        _hasStationStop = false;
                        OnStationArrived?.Invoke();
                    }

                    UpdateATOState(currentSpeed, effectiveTargetSpeed);
                    return effectiveTargetSpeed;
                }
            }

            effectiveTargetSpeed = Mathf.Max(effectiveTargetSpeed, 0f);
            UpdateATOState(currentSpeed, effectiveTargetSpeed);

            return effectiveTargetSpeed;
        }

        public List<BrakingPoint> GetBrakingCurvePoints()
        {
            return _currentBrakingCurve;
        }

        public float CalculateStationBrakingProfile(float currentSpeed, float distanceToStop)
        {
            if (distanceToStop <= 0f || currentSpeed <= 0f) return 0f;

            float deceleration = _physicsConfig.maxDeceleration;

            if (distanceToStop > FineBrakingDistance)
            {
                float cruiseSpeed = Mathf.Min(_targetSpeed, currentSpeed);

                float brakingDistance = BrakingCurve.CalculateSafeBrakingDistance(
                    cruiseSpeed, 0f, deceleration, _reactionTime);

                if (distanceToStop > brakingDistance + FineBrakingDistance)
                {
                    return cruiseSpeed;
                }

                float speedFromStop = Mathf.Sqrt(2f * deceleration * (distanceToStop - FineBrakingDistance));
                return Mathf.Min(cruiseSpeed, speedFromStop);
            }

            float approachSpeed = distanceToStop * 0.3f;
            float fineBrakeSpeed = Mathf.Sqrt(2f * deceleration * distanceToStop);
            return Mathf.Min(approachSpeed, fineBrakeSpeed, currentSpeed);
        }

        private float CalculateEffectiveTargetSpeed(float currentSpeed, float currentDistance)
        {
            float effectiveSpeed = _targetSpeed;

            float restrictionLimit = _restrictionProfile.GetEffectiveSpeedLimit(currentDistance);
            if (restrictionLimit < float.MaxValue)
            {
                effectiveSpeed = Mathf.Min(effectiveSpeed, restrictionLimit);
            }

            return effectiveSpeed;
        }

        private void UpdateBrakingCurve(float currentSpeed, float currentDistance)
        {
            var upcomingRestrictions = _restrictionProfile.GetUpcomingRestrictions(currentDistance, 2000f);

            if (upcomingRestrictions.Count > 0)
            {
                var restrictionArray = upcomingRestrictions.ToArray();
                _currentBrakingCurve = BrakingCurve.CalculateSpeedProfileWithRestrictions(
                    currentSpeed, currentDistance, restrictionArray,
                    _physicsConfig.maxDeceleration, _reactionTime);
            }
            else
            {
                _currentBrakingCurve = BrakingCurve.CalculateServiceBrakingCurve(
                    currentSpeed, currentDistance, _targetSpeed,
                    _physicsConfig.maxDeceleration, _reactionTime);
            }

            if (_hasStationStop)
            {
                float distanceToStop = _stationStopPosition - currentDistance;
                if (distanceToStop > 0f)
                {
                    var stationCurve = BrakingCurve.CalculateServiceBrakingCurve(
                        currentSpeed, currentDistance, 0f,
                        _physicsConfig.maxDeceleration, _reactionTime);
                    MergeBrakingCurves(stationCurve);
                }
            }
        }

        private void MergeBrakingCurves(List<BrakingPoint> otherCurve)
        {
            for (int i = 0; i < _currentBrakingCurve.Count; i++)
            {
                float otherSpeed = BrakingCurve.GetTargetSpeedAtDistance(_currentBrakingCurve[i].distance, otherCurve);
                if (otherSpeed < _currentBrakingCurve[i].speed)
                {
                    var pt = _currentBrakingCurve[i];
                    pt.speed = otherSpeed;
                    pt.isLimiting = true;
                    pt.source = "StationStop";
                    _currentBrakingCurve[i] = pt;
                }
            }
        }

        private bool IsTouchingBrakingCurve(float currentSpeed, float currentDistance)
        {
            if (_currentBrakingCurve == null || _currentBrakingCurve.Count == 0) return false;

            float curveSpeed = BrakingCurve.GetTargetSpeedAtDistance(currentDistance, _currentBrakingCurve);
            return currentSpeed >= curveSpeed - CoastingSpeedThreshold;
        }

        private void UpdateATOState(float currentSpeed, float effectiveTargetSpeed)
        {
            float speedDiff = effectiveTargetSpeed - currentSpeed;

            if (currentSpeed < 0.01f && effectiveTargetSpeed < 0.01f)
            {
                _currentState = ATOState.Idle;
            }
            else if (speedDiff > CoastingSpeedThreshold)
            {
                _currentState = ATOState.Accelerating;
            }
            else if (speedDiff < -CoastingSpeedThreshold)
            {
                _currentState = ATOState.TargetBraking;
            }
            else if (Mathf.Abs(speedDiff) <= CoastingSpeedThreshold && currentSpeed > 0.01f)
            {
                _currentState = ATOState.Cruising;
            }
            else
            {
                _currentState = ATOState.Coasting;
            }
        }

        private void SuperviseSpeed()
        {
            if (_trainController == null || _physicsConfig == null) return;

            float currentSpeed = _trainController.CurrentSpeed;
            float currentDistance = _trainController.CurrentDistance;

            float effectiveLimit = _targetSpeed;
            float restrictionLimit = _restrictionProfile.GetEffectiveSpeedLimit(currentDistance);
            if (restrictionLimit < float.MaxValue)
            {
                effectiveLimit = Mathf.Min(effectiveLimit, restrictionLimit);
            }

            if (currentSpeed > effectiveLimit + 2f)
            {
                OnSpeedExceeded?.Invoke();
            }

            float emergencyBrakeDistance = BrakingCurve.CalculateSafeBrakingDistance(
                currentSpeed, 0f, _physicsConfig.emergencyDeceleration, 0f);

            if (_hasStationStop)
            {
                float distanceToStop = _stationStopPosition - currentDistance;
                if (distanceToStop > 0f && distanceToStop < emergencyBrakeDistance * 0.5f && currentSpeed > 0.5f)
                {
                    _trainController.ApplyEmergencyBrake();
                    OnEmergencyBrakeApplied?.Invoke();
                }
            }
        }
    }
}
