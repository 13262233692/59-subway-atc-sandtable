using System;
using System.Collections.Generic;
using UnityEngine;
using CBTC.Sandtable.Track;
using CBTC.Sandtable.Train;

namespace CBTC.Sandtable.Signal
{
    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical,
        Emergency
    }

    public struct AlertInfo
    {
        public string alertType;
        public string message;
        public AlertSeverity severity;
        public float timestamp;

        public AlertInfo(string alertType, string message, AlertSeverity severity, float timestamp)
        {
            this.alertType = alertType;
            this.message = message;
            this.severity = severity;
            this.timestamp = timestamp;
        }
    }

    public struct CBTCSystemStatus
    {
        public int totalTrains;
        public int activeTrains;
        public int freeCircuits;
        public int occupiedCircuits;
        public int faultyCircuits;
        public List<AlertInfo> alerts;
    }

    public class CBTCZoneController : MonoBehaviour
    {
        [SerializeField] private TrackNetwork _trackNetwork;
        [SerializeField] private MovingBlockController _movingBlockController;
        [SerializeField] private float _updateInterval = 0.5f;

        private SignalAuthorityManager _authorityManager = new SignalAuthorityManager();
        private List<TrackCircuit> _trackCircuits = new List<TrackCircuit>();
        private List<AlertInfo> _activeAlerts = new List<AlertInfo>();
        private Dictionary<string, TrainPositionInfo> _trainPositions = new Dictionary<string, TrainPositionInfo>();
        private float _lastUpdateTime;
        private bool _initialized;

        public TrackNetwork TrackNetwork => _trackNetwork;
        public MovingBlockController MovingBlockController => _movingBlockController;
        public SignalAuthorityManager AuthorityManager => _authorityManager;
        public List<TrackCircuit> TrackCircuits => _trackCircuits;

        public void InitializeCBTC()
        {
            if (_trackNetwork == null)
            {
                _trackNetwork = FindObjectOfType<TrackNetwork>();
            }
            if (_movingBlockController == null)
            {
                _movingBlockController = FindObjectOfType<MovingBlockController>();
            }

            _trackCircuits.Clear();
            _trainPositions.Clear();
            _activeAlerts.Clear();
            _authorityManager = new SignalAuthorityManager();
            _authorityManager.SetTrackCircuits(_trackCircuits);
            _lastUpdateTime = Time.time;
            _initialized = true;
        }

        public void OnFixedUpdate(float deltaTime)
        {
            if (!_initialized) return;

            _lastUpdateTime += deltaTime;

            UpdateTrackCircuitStates();
            _authorityManager.UpdateAuthorities();

            if (_movingBlockController != null)
            {
                _movingBlockController.UpdateTrainPositions();
                UpdateMovementAuthorities();
            }

            ValidateAllAuthorities();
            ConflictDetection();
        }

        private void UpdateMovementAuthorities()
        {
            if (_movingBlockController == null) return;

            for (int i = 0; i < _movingBlockController.ManagedTrains.Count; i++)
            {
                SplineTrainController train = _movingBlockController.ManagedTrains[i];
                string trainId = train.gameObject.GetInstanceID().ToString();
                _movingBlockController.GetSafeSpeedForTrain(trainId);
            }
        }

        public void HandleTrainPositionUpdate(string trainId, string segmentId, float distance, float speed)
        {
            TrainPositionInfo pos = new TrainPositionInfo
            {
                trainId = trainId,
                segmentId = segmentId,
                distance = distance,
                speed = speed,
                trainLength = 22f
            };
            _trainPositions[trainId] = pos;

            for (int i = 0; i < _trackCircuits.Count; i++)
            {
                _trackCircuits[i].UpdateState(new List<TrainPositionInfo>(_trainPositions.Values));
            }
        }

        public void ReportEmergency(string trainId, string reason)
        {
            AddAlert("Emergency", $"Train {trainId}: {reason}", AlertSeverity.Emergency, Time.time);

            _authorityManager.IssueAuthority(trainId, new SignalAuthority(
                Guid.NewGuid().ToString(), trainId, "", 0f, 0f,
                SignalAuthorityType.EmergencyStop, Time.time, 0f));

            if (_movingBlockController == null) return;

            TrainPositionInfo? emergencyTrainPos = null;
            if (_trainPositions.TryGetValue(trainId, out TrainPositionInfo pos))
            {
                emergencyTrainPos = pos;
            }

            for (int i = 0; i < _movingBlockController.ManagedTrains.Count; i++)
            {
                SplineTrainController train = _movingBlockController.ManagedTrains[i];
                if (train == null) continue;

                string otherTrainId = train.gameObject.GetInstanceID().ToString();
                if (otherTrainId == trainId) continue;

                if (emergencyTrainPos.HasValue)
                {
                    float otherDistance = train.CurrentDistance;
                    if (otherDistance < emergencyTrainPos.Value.distance)
                    {
                        train.ApplyEmergencyBrake();
                        _authorityManager.IssueAuthority(otherTrainId, new SignalAuthority(
                            Guid.NewGuid().ToString(), otherTrainId, "", 0f, 0f,
                            SignalAuthorityType.EmergencyStop, Time.time, 0f));
                    }
                }
            }
        }

        public CBTCSystemStatus GetSystemStatus()
        {
            CBTCSystemStatus status = new CBTCSystemStatus();
            status.totalTrains = _trainPositions.Count;
            status.activeTrains = 0;

            foreach (var kvp in _trainPositions)
            {
                if (kvp.Value.speed > 0.01f)
                {
                    status.activeTrains++;
                }
            }

            status.freeCircuits = 0;
            status.occupiedCircuits = 0;
            status.faultyCircuits = 0;

            for (int i = 0; i < _trackCircuits.Count; i++)
            {
                switch (_trackCircuits[i].state)
                {
                    case TrackCircuitState.Clear:
                        status.freeCircuits++;
                        break;
                    case TrackCircuitState.Occupied:
                        status.occupiedCircuits++;
                        break;
                    case TrackCircuitState.Faulty:
                        status.faultyCircuits++;
                        break;
                }
            }

            status.alerts = new List<AlertInfo>(_activeAlerts);
            return status;
        }

        public void AddTrackCircuit(TrackCircuit circuit)
        {
            _trackCircuits.Add(circuit);
            _authorityManager.SetTrackCircuits(_trackCircuits);
        }

        public void RemoveTrackCircuit(string circuitId)
        {
            for (int i = _trackCircuits.Count - 1; i >= 0; i--)
            {
                if (_trackCircuits[i].circuitId == circuitId)
                {
                    _trackCircuits.RemoveAt(i);
                    break;
                }
            }
            _authorityManager.SetTrackCircuits(_trackCircuits);
        }

        private void UpdateTrackCircuitStates()
        {
            List<TrainPositionInfo> positions = new List<TrainPositionInfo>(_trainPositions.Values);
            for (int i = 0; i < _trackCircuits.Count; i++)
            {
                _trackCircuits[i].UpdateState(positions);
            }
        }

        private void ValidateAllAuthorities()
        {
            List<string> trainIds = new List<string>(_authorityManager.ActiveAuthorities.Keys);
            for (int i = 0; i < trainIds.Count; i++)
            {
                if (!_authorityManager.ValidateAuthority(trainIds[i]))
                {
                    AddAlert("AuthorityInvalid", $"Authority for train {trainIds[i]} is no longer valid",
                        AlertSeverity.Warning, Time.time);
                }
            }
        }

        private List<AlertInfo> ConflictDetection()
        {
            List<AlertInfo> conflicts = new List<AlertInfo>();
            List<SignalAuthority> authorities = new List<SignalAuthority>(_authorityManager.ActiveAuthorities.Values);

            for (int i = 0; i < authorities.Count; i++)
            {
                for (int j = i + 1; j < authorities.Count; j++)
                {
                    SignalAuthority a = authorities[i];
                    SignalAuthority b = authorities[j];

                    if (a.trainId == b.trainId) continue;

                    if (a.endSegmentId == b.endSegmentId &&
                        Mathf.Abs(a.endDistance - b.endDistance) < 50f)
                    {
                        AlertInfo conflict = new AlertInfo(
                            "Conflict",
                            $"Authority overlap: train {a.trainId} and train {b.trainId} at segment {a.endSegmentId}",
                            AlertSeverity.Critical,
                            Time.time);
                        conflicts.Add(conflict);
                        AddAlert(conflict);
                    }
                }
            }

            return conflicts;
        }

        private void AddAlert(AlertInfo alert)
        {
            _activeAlerts.Add(alert);
            if (_activeAlerts.Count > 100)
            {
                _activeAlerts.RemoveAt(0);
            }
        }

        private void AddAlert(string alertType, string message, AlertSeverity severity, float timestamp)
        {
            AddAlert(new AlertInfo(alertType, message, severity, timestamp));
        }
    }
}
