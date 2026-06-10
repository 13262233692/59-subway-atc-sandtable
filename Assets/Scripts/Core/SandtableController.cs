using System;
using System.Collections.Generic;
using UnityEngine;
using CBTC.Sandtable.Track;
using CBTC.Sandtable.Train;
using CBTC.Sandtable.Signal;

namespace CBTC.Sandtable.Core
{
    public class SandtableController : MonoBehaviour
    {
        [SerializeField] private TrackNetwork _trackNetwork;
        [SerializeField] private CBTCZoneController _zoneController;
        [SerializeField] private MovingBlockController _movingBlockController;
        [SerializeField] private TrainPhysicsConfig _defaultTrainConfig;
        [SerializeField] private GameObject _trainPrefab;
        [SerializeField] private float _simulationTimeScale = 1f;
        [SerializeField] private bool _pauseSimulation;

        private Dictionary<string, TrainInstance> _trains = new Dictionary<string, TrainInstance>();
        private List<string> _trainOrder = new List<string>();
        private float _simulationTime;
        private bool _initialized;

        public TrackNetwork TrackNetwork => _trackNetwork;
        public CBTCZoneController ZoneController => _zoneController;
        public MovingBlockController MovingBlockController => _movingBlockController;
        public float SimulationTime => _simulationTime;
        public float SimulationTimeScale
        {
            get => _simulationTimeScale;
            set => _simulationTimeScale = Mathf.Max(0f, value);
        }
        public bool IsPaused
        {
            get => _pauseSimulation;
            set => _pauseSimulation = value;
        }
        public int TrainCount => _trains.Count;

        private void Awake()
        {
            Initialize();
        }

        private void Update()
        {
            if (_pauseSimulation || !_initialized) return;

            float dt = Time.deltaTime * _simulationTimeScale;
            _simulationTime += dt;

            if (_movingBlockController != null)
                _movingBlockController.UpdateTrainPositions();

            if (_zoneController != null)
                _zoneController.OnFixedUpdate(dt);

            UpdateTrains(dt);
        }

        public void Initialize()
        {
            if (_initialized) return;

            if (_trackNetwork == null)
                _trackNetwork = FindObjectOfType<TrackNetwork>();
            if (_zoneController == null)
                _zoneController = FindObjectOfType<CBTCZoneController>();
            if (_movingBlockController == null)
                _movingBlockController = FindObjectOfType<MovingBlockController>();

            if (_trackNetwork != null)
                _trackNetwork.Initialize();

            if (_zoneController != null)
                _zoneController.InitializeCBTC();

            _initialized = true;
        }

        public string SpawnTrain(string trainId, string segmentId, float startDistance, float startSpeed = 0f)
        {
            if (!_initialized) Initialize();

            if (_trains.ContainsKey(trainId))
            {
                Debug.LogWarning($"Train {trainId} already exists");
                return trainId;
            }

            TrackSegment segment = _trackNetwork != null ? _trackNetwork.GetSegment(segmentId) : null;
            if (segment == null)
            {
                Debug.LogError($"Segment {segmentId} not found");
                return null;
            }

            GameObject trainObj;
            if (_trainPrefab != null)
            {
                trainObj = Instantiate(_trainPrefab);
            }
            else
            {
                trainObj = new GameObject($"Train_{trainId}");
            }

            trainObj.transform.SetParent(this.transform);

            SplineTrainController splineCtrl = trainObj.GetComponent<SplineTrainController>();
            if (splineCtrl == null)
                splineCtrl = trainObj.AddComponent<SplineTrainController>();

            TrainPhysicsConfig config = _defaultTrainConfig;
            splineCtrl.Configure(config, _trackNetwork);
            splineCtrl.Initialize(segment, startDistance, startSpeed);

            ATOController atoCtrl = trainObj.GetComponent<ATOController>();
            if (atoCtrl == null)
                atoCtrl = trainObj.AddComponent<ATOController>();

            atoCtrl.Configure(splineCtrl, config);

            TrainInstance instance = new TrainInstance
            {
                trainId = trainId,
                gameObject = trainObj,
                splineController = splineCtrl,
                atoController = atoCtrl,
                config = config
            };

            _trains[trainId] = instance;
            _trainOrder.Add(trainId);

            if (_movingBlockController != null)
                _movingBlockController.RegisterTrain(splineCtrl, config);

            return trainId;
        }

        public void RemoveTrain(string trainId)
        {
            if (!_trains.TryGetValue(trainId, out TrainInstance instance)) return;

            if (_movingBlockController != null)
                _movingBlockController.UnregisterTrain(instance.splineController);

            if (instance.gameObject != null)
                Destroy(instance.gameObject);

            _trains.Remove(trainId);
            _trainOrder.Remove(trainId);
        }

        public void DispatchTrain(string trainId, string targetNodeId, float targetSpeed)
        {
            if (!_trains.TryGetValue(trainId, out TrainInstance instance)) return;

            instance.atoController.SetMode(ATOMode.Automatic);
            instance.atoController.SetTargetSpeed(targetSpeed);
        }

        public void StopTrain(string trainId)
        {
            if (!_trains.TryGetValue(trainId, out TrainInstance instance)) return;

            instance.atoController.SetMode(ATOMode.Manual);
            instance.splineController.UpdateMovement(0f, 0f);
        }

        public void EmergencyStopTrain(string trainId)
        {
            if (!_trains.TryGetValue(trainId, out TrainInstance instance)) return;

            instance.splineController.ApplyEmergencyBrake();
        }

        public void SetTrainStationStop(string trainId, float stopDistance)
        {
            if (!_trains.TryGetValue(trainId, out TrainInstance instance)) return;

            instance.atoController.SetStationStop(stopDistance);
        }

        public void ReportEmergency(string trainId, string reason)
        {
            if (_zoneController != null)
                _zoneController.ReportEmergency(trainId, reason);
        }

        public TrainRuntimeInfo GetTrainInfo(string trainId)
        {
            if (!_trains.TryGetValue(trainId, out TrainInstance instance))
                return default;

            var info = new TrainRuntimeInfo
            {
                trainId = trainId,
                segmentId = instance.splineController.CurrentSegmentId,
                distance = instance.splineController.CurrentDistance,
                speed = instance.splineController.CurrentSpeed,
                movementState = instance.splineController.CurrentState,
                atoMode = instance.atoController.CurrentMode,
                atoState = instance.atoController.CurrentState,
                position = instance.splineController.transform.position,
                headPosition = instance.splineController.GetHeadPosition(),
                tailPosition = instance.splineController.GetTailPosition()
            };

            if (_movingBlockController != null)
            {
                string tid = instance.splineController.gameObject.GetInstanceID().ToString();
                info.headway = _movingBlockController.GetTrainHeadway(tid);
                info.movementAuthority = _movingBlockController.GetMovementAuthority(tid);
            }

            return info;
        }

        public List<TrainRuntimeInfo> GetAllTrainInfo()
        {
            List<TrainRuntimeInfo> infos = new List<TrainRuntimeInfo>();
            for (int i = 0; i < _trainOrder.Count; i++)
            {
                infos.Add(GetTrainInfo(_trainOrder[i]));
            }
            return infos;
        }

        public CBTCSystemStatus GetSystemStatus()
        {
            if (_zoneController != null)
                return _zoneController.GetSystemStatus();
            return default;
        }

        public void SetSpeedRestriction(float startDistance, float endDistance, float speedLimit, RestrictionType type)
        {
            for (int i = 0; i < _trainOrder.Count; i++)
            {
                if (_trains.TryGetValue(_trainOrder[i], out TrainInstance instance))
                {
                    instance.atoController.RestrictionProfile.AddRestriction(
                        new SpeedRestriction(startDistance, endDistance, speedLimit, type));
                }
            }
        }

        private void UpdateTrains(float dt)
        {
            for (int i = 0; i < _trainOrder.Count; i++)
            {
                if (!_trains.TryGetValue(_trainOrder[i], out TrainInstance instance)) continue;

                float atoTargetSpeed = instance.atoController.ComputeTargetSpeed(dt);
                float finalTargetSpeed = atoTargetSpeed;

                if (_movingBlockController != null)
                {
                    string tid = instance.splineController.gameObject.GetInstanceID().ToString();
                    MovementAuthority ma = _movingBlockController.GetMovementAuthority(tid);
                    if (ma.authorityType == AuthorityType.Stop)
                    {
                        finalTargetSpeed = 0f;
                    }
                    else if (ma.authorityType == AuthorityType.Restricted)
                    {
                        finalTargetSpeed = Mathf.Min(atoTargetSpeed, ma.maxSpeed);
                    }
                }

                instance.splineController.UpdateMovement(dt, finalTargetSpeed);

                if (_zoneController != null)
                {
                    _zoneController.HandleTrainPositionUpdate(
                        instance.trainId,
                        instance.splineController.CurrentSegmentId,
                        instance.splineController.CurrentDistance,
                        instance.splineController.CurrentSpeed);
                }
            }
        }

        private void OnDestroy()
        {
            List<string> ids = new List<string>(_trains.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                RemoveTrain(ids[i]);
            }
        }
    }

    public class TrainInstance
    {
        public string trainId;
        public GameObject gameObject;
        public SplineTrainController splineController;
        public ATOController atoController;
        public TrainPhysicsConfig config;
    }

    [Serializable]
    public struct TrainRuntimeInfo
    {
        public string trainId;
        public string segmentId;
        public float distance;
        public float speed;
        public TrainMovementState movementState;
        public ATOMode atoMode;
        public ATOState atoState;
        public Vector3 position;
        public Vector3 headPosition;
        public Vector3 tailPosition;
        public float headway;
        public MovementAuthority movementAuthority;
    }
}
