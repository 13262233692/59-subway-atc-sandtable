using System.Collections.Generic;
using UnityEngine;
using CBTC.Sandtable.Train;

namespace CBTC.Sandtable.Signal
{
    public enum AuthorityType
    {
        Full,
        Restricted,
        Stop
    }

    public struct MovementAuthority
    {
        public float endDistance;
        public string endSegmentId;
        public float maxSpeed;
        public AuthorityType authorityType;

        public MovementAuthority(float endDistance, string endSegmentId, float maxSpeed, AuthorityType authorityType)
        {
            this.endDistance = endDistance;
            this.endSegmentId = endSegmentId;
            this.maxSpeed = maxSpeed;
            this.authorityType = authorityType;
        }
    }

    public struct TrainPositionInfo
    {
        public string trainId;
        public string segmentId;
        public float distance;
        public float speed;
        public float trainLength;
    }

    public class MovingBlockController : MonoBehaviour
    {
        [SerializeField] private float _minSafeDistance = 50f;
        [SerializeField] private float _safeTimeHeadway = 30f;
        [SerializeField] private float _reactionTime = 2.0f;

        private List<SplineTrainController> _managedTrains = new List<SplineTrainController>();
        private Dictionary<string, TrainPhysicsConfig> _trainConfigs = new Dictionary<string, TrainPhysicsConfig>();
        private Dictionary<string, TrainPositionInfo> _trainPositions = new Dictionary<string, TrainPositionInfo>();
        private Dictionary<string, MovementAuthority> _movementAuthorities = new Dictionary<string, MovementAuthority>();

        public float MinSafeDistance => _minSafeDistance;
        public float SafeTimeHeadway => _safeTimeHeadway;
        public List<SplineTrainController> ManagedTrains => _managedTrains;

        public void RegisterTrain(SplineTrainController train)
        {
            RegisterTrain(train, null);
        }

        public void RegisterTrain(SplineTrainController train, TrainPhysicsConfig config)
        {
            if (train == null || _managedTrains.Contains(train)) return;
            _managedTrains.Add(train);
            string trainId = GetTrainId(train);
            if (trainId != null && config != null)
            {
                _trainConfigs[trainId] = config;
            }
        }

        public void UnregisterTrain(SplineTrainController train)
        {
            if (train == null) return;
            _managedTrains.Remove(train);
            string trainId = GetTrainId(train);
            if (trainId != null)
            {
                _trainPositions.Remove(trainId);
                _movementAuthorities.Remove(trainId);
                _trainConfigs.Remove(trainId);
            }
        }

        public float GetSafeSpeedForTrain(string trainId)
        {
            TrainPositionInfo? trainInfo = GetTrainPositionInfo(trainId);
            if (!trainInfo.HasValue) return 0f;

            TrainPositionInfo info = trainInfo.Value;

            TrainPositionInfo? precedingTrain = FindPrecedingTrain(info);
            if (!precedingTrain.HasValue)
            {
                _movementAuthorities[trainId] = new MovementAuthority(
                    float.MaxValue, "", float.MaxValue, AuthorityType.Full);
                return float.MaxValue;
            }

            TrainPositionInfo preceding = precedingTrain.Value;
            float precedingTailDistance = preceding.distance - preceding.trainLength * 0.5f;
            float maEndDistance = precedingTailDistance - _minSafeDistance;

            if (maEndDistance <= info.distance)
            {
                _movementAuthorities[trainId] = new MovementAuthority(
                    info.distance, info.segmentId, 0f, AuthorityType.Stop);
                return 0f;
            }

            float distanceToMAEnd = maEndDistance - info.distance;

            TrainPhysicsConfig config = GetTrainConfig(trainId);
            float deceleration = config != null ? config.maxDeceleration : 1.2f;
            float maxSpeed = config != null ? config.maxSpeed : 22.22f;

            float safeBrakingDistance = BrakingCurve.CalculateSafeBrakingDistance(
                info.speed, 0f, deceleration, _reactionTime);

            if (safeBrakingDistance > distanceToMAEnd)
            {
                float maxSafeSpeed = Mathf.Sqrt(2f * deceleration * distanceToMAEnd);
                maxSafeSpeed = Mathf.Min(maxSafeSpeed, maxSpeed);

                _movementAuthorities[trainId] = new MovementAuthority(
                    maEndDistance, preceding.segmentId, maxSafeSpeed, AuthorityType.Restricted);
                return maxSafeSpeed;
            }

            _movementAuthorities[trainId] = new MovementAuthority(
                maEndDistance, preceding.segmentId, maxSpeed, AuthorityType.Full);
            return maxSpeed;
        }

        public MovementAuthority GetMovementAuthority(string trainId)
        {
            if (_movementAuthorities.TryGetValue(trainId, out MovementAuthority ma))
            {
                return ma;
            }
            return new MovementAuthority(0f, "", 0f, AuthorityType.Stop);
        }

        public void UpdateAllTrains(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            UpdateTrainPositions();

            for (int i = 0; i < _managedTrains.Count; i++)
            {
                SplineTrainController train = _managedTrains[i];
                string trainId = GetTrainId(train);
                if (trainId == null) continue;

                float safeSpeed = GetSafeSpeedForTrain(trainId);

                if (safeSpeed < 0.01f)
                {
                    train.FixedUpdateMovement(deltaTime, 0f);
                }
                else
                {
                    train.FixedUpdateMovement(deltaTime, safeSpeed);
                }
            }
        }

        public float GetTrainHeadway(string trainId)
        {
            TrainPositionInfo? trainInfo = GetTrainPositionInfo(trainId);
            if (!trainInfo.HasValue) return float.MaxValue;

            TrainPositionInfo info = trainInfo.Value;
            TrainPositionInfo? precedingTrain = FindPrecedingTrain(info);
            if (!precedingTrain.HasValue) return float.MaxValue;

            TrainPositionInfo preceding = precedingTrain.Value;
            float spatialHeadway = preceding.distance - info.distance;

            if (info.speed > 0.01f)
            {
                float temporalHeadway = spatialHeadway / info.speed;
                return temporalHeadway;
            }

            return float.MaxValue;
        }

        public void UpdateTrainPositions()
        {
            _trainPositions.Clear();

            for (int i = 0; i < _managedTrains.Count; i++)
            {
                SplineTrainController train = _managedTrains[i];
                string trainId = GetTrainId(train);
                if (trainId == null) continue;

                TrainPhysicsConfig config = GetTrainConfig(trainId);
                float trainLength = config != null ? config.length : 22f;

                _trainPositions[trainId] = new TrainPositionInfo
                {
                    trainId = trainId,
                    segmentId = train.CurrentSegmentId,
                    distance = train.CurrentDistance,
                    speed = train.CurrentSpeed,
                    trainLength = trainLength
                };
            }
        }

        private TrainPositionInfo? FindPrecedingTrain(TrainPositionInfo trainInfo)
        {
            TrainPositionInfo? closest = null;
            float closestDistance = float.MaxValue;

            foreach (var kvp in _trainPositions)
            {
                if (kvp.Key == trainInfo.trainId) continue;

                float distanceAhead = kvp.Value.distance - trainInfo.distance;
                if (distanceAhead > 0f && distanceAhead < closestDistance)
                {
                    closestDistance = distanceAhead;
                    closest = kvp.Value;
                }
            }

            return closest;
        }

        private TrainPositionInfo? GetTrainPositionInfo(string trainId)
        {
            if (_trainPositions.TryGetValue(trainId, out TrainPositionInfo info))
            {
                return info;
            }
            return null;
        }

        private string GetTrainId(SplineTrainController train)
        {
            if (train == null) return null;
            return train.gameObject.GetInstanceID().ToString();
        }

        private TrainPhysicsConfig GetTrainConfig(string trainId)
        {
            if (_trainConfigs.TryGetValue(trainId, out TrainPhysicsConfig config))
            {
                return config;
            }
            return null;
        }
    }
}
