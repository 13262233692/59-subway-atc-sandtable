using System.Collections.Generic;
using UnityEngine;
using CBTC.Sandtable.Signal;

namespace CBTC.Sandtable.Core
{
    public enum DispatchMode
    {
        Scheduled,
        Headway,
        Manual
    }

    [Serializable]
    public struct StationScheduleEntry
    {
        public string stationNodeId;
        public float arrivalTime;
        public float dwellTime;
        public float departureTime;
    }

    [Serializable]
    public struct TrainSchedule
    {
        public string trainId;
        public string startSegmentId;
        public float startDistance;
        public string endNodeId;
        public float lineSpeed;
        public List<StationScheduleEntry> stationSchedule;
    }

    public class TrainDispatcher : MonoBehaviour
    {
        [SerializeField] private SandtableController _sandtableController;
        [SerializeField] private DispatchMode _dispatchMode = DispatchMode.Headway;
        [SerializeField] private float _headwayInterval = 120f;
        [SerializeField] private float _maxTrains = 8;
        [SerializeField] private float _lineSpeed = 22.22f;

        private List<TrainSchedule> _schedules = new List<TrainSchedule>();
        private Dictionary<string, float> _trainTimers = new Dictionary<string, float>();
        private Dictionary<string, int> _trainScheduleIndex = new Dictionary<string, int>();
        private float _nextDispatchTime;
        private int _dispatchedCount;

        public DispatchMode Mode => _dispatchMode;
        public int ActiveTrainCount => _sandtableController != null ? _sandtableController.TrainCount : 0;

        public void LoadSchedules(List<TrainSchedule> schedules)
        {
            _schedules = schedules ?? new List<TrainSchedule>();
        }

        public void StartScheduledDispatch()
        {
            _dispatchMode = DispatchMode.Scheduled;
            _dispatchedCount = 0;
            _nextDispatchTime = 0f;
        }

        public void StartHeadwayDispatch(string startSegmentId, float startDistance, string endNodeId)
        {
            _dispatchMode = DispatchMode.Headway;
            _dispatchedCount = 0;
            _nextDispatchTime = 0f;

            _schedules.Clear();
            for (int i = 0; i < _maxTrains; i++)
            {
                var schedule = new TrainSchedule
                {
                    trainId = $"Train_{i + 1:D3}",
                    startSegmentId = startSegmentId,
                    startDistance = startDistance,
                    endNodeId = endNodeId,
                    lineSpeed = _lineSpeed,
                    stationSchedule = new List<StationScheduleEntry>()
                };
                _schedules.Add(schedule);
            }
        }

        public void StopAllDispatch()
        {
            _dispatchMode = DispatchMode.Manual;
        }

        private void Update()
        {
            if (_sandtableController == null) return;
            if (_dispatchMode == DispatchMode.Manual) return;

            float simTime = _sandtableController.SimulationTime;

            switch (_dispatchMode)
            {
                case DispatchMode.Scheduled:
                    UpdateScheduledDispatch(simTime);
                    break;
                case DispatchMode.Headway:
                    UpdateHeadwayDispatch(simTime);
                    break;
            }

            UpdateTrainSchedules(simTime);
        }

        private void UpdateScheduledDispatch(float simTime)
        {
            for (int i = _dispatchedCount; i < _schedules.Count; i++)
            {
                if (i >= _maxTrains) break;

                var schedule = _schedules[i];
                if (schedule.stationSchedule == null || schedule.stationSchedule.Count == 0) continue;

                float dispatchTime = schedule.stationSchedule[0].arrivalTime - 300f;
                if (simTime >= dispatchTime)
                {
                    DispatchFromSchedule(schedule);
                    _dispatchedCount++;
                }
            }
        }

        private void UpdateHeadwayDispatch(float simTime)
        {
            if (_dispatchedCount >= _maxTrains) return;
            if (_dispatchedCount >= _schedules.Count) return;

            if (simTime >= _nextDispatchTime)
            {
                var schedule = _schedules[_dispatchedCount];
                DispatchFromSchedule(schedule);
                _dispatchedCount++;
                _nextDispatchTime = simTime + _headwayInterval;
            }
        }

        private void DispatchFromSchedule(TrainSchedule schedule)
        {
            string trainId = _sandtableController.SpawnTrain(
                schedule.trainId,
                schedule.startSegmentId,
                schedule.startDistance,
                0f);

            if (!string.IsNullOrEmpty(trainId))
            {
                _sandtableController.DispatchTrain(trainId, schedule.endNodeId, schedule.lineSpeed);
                _trainScheduleIndex[trainId] = _dispatchedCount;
                _trainTimers[trainId] = 0f;

                if (schedule.stationSchedule != null && schedule.stationSchedule.Count > 0)
                {
                    float firstStationDist = schedule.stationSchedule[0].arrivalTime;
                    _sandtableController.SetTrainStationStop(trainId, firstStationDist);
                }
            }
        }

        private void UpdateTrainSchedules(float simTime)
        {
            List<string> trainIds = new List<string>(_trainTimers.Keys);
            for (int i = 0; i < trainIds.Count; i++)
            {
                string trainId = trainIds[i];
                _trainTimers[trainId] += Time.deltaTime;

                var info = _sandtableController.GetTrainInfo(trainId);
                if (info.atoState == ATOState.DoorOperation)
                {
                    if (!_trainScheduleIndex.TryGetValue(trainId, out int schedIdx)) continue;
                    if (schedIdx >= _schedules.Count) continue;

                    var schedule = _schedules[schedIdx];
                    if (schedule.stationSchedule == null || schedule.stationSchedule.Count == 0) continue;

                    _sandtableController.DispatchTrain(trainId, schedule.endNodeId, schedule.lineSpeed);
                }
            }
        }
    }
}
