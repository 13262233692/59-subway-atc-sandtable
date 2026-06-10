using System;
using System.Collections.Generic;

namespace CBTC.Sandtable.Signal
{
    public enum TrackCircuitState
    {
        Clear,
        Occupied,
        Faulty
    }

    public class TrackCircuit
    {
        public string circuitId;
        public string segmentId;
        public float startDistance;
        public float endDistance;
        public TrackCircuitState state = TrackCircuitState.Clear;
        public string occupyingTrainId;

        public event Action<string> OnCircuitOccupied;
        public event Action<string> OnCircuitCleared;
        public event Action<string> OnCircuitFault;

        public TrackCircuit(string circuitId, string segmentId, float startDistance, float endDistance)
        {
            this.circuitId = circuitId;
            this.segmentId = segmentId;
            this.startDistance = startDistance;
            this.endDistance = endDistance;
        }

        public void UpdateState(List<TrainPositionInfo> trainPositions)
        {
            if (state == TrackCircuitState.Faulty) return;

            string newOccupyingTrainId = null;

            for (int i = 0; i < trainPositions.Count; i++)
            {
                TrainPositionInfo pos = trainPositions[i];
                if (pos.segmentId != segmentId) continue;

                float trainStart = pos.distance - pos.trainLength * 0.5f;
                float trainEnd = pos.distance + pos.trainLength * 0.5f;

                if (trainEnd > startDistance && trainStart < endDistance)
                {
                    newOccupyingTrainId = pos.trainId;
                    break;
                }
            }

            if (newOccupyingTrainId != null && state != TrackCircuitState.Occupied)
            {
                Occupy(newOccupyingTrainId);
            }
            else if (newOccupyingTrainId == null && state == TrackCircuitState.Occupied)
            {
                Clear();
            }
            else if (newOccupyingTrainId != null)
            {
                occupyingTrainId = newOccupyingTrainId;
            }
        }

        public bool IsClear()
        {
            return state == TrackCircuitState.Clear;
        }

        public void Occupy(string trainId)
        {
            if (state == TrackCircuitState.Faulty) return;
            occupyingTrainId = trainId;
            state = TrackCircuitState.Occupied;
            OnCircuitOccupied?.Invoke(circuitId);
        }

        public void Clear()
        {
            if (state == TrackCircuitState.Faulty) return;
            occupyingTrainId = null;
            state = TrackCircuitState.Clear;
            OnCircuitCleared?.Invoke(circuitId);
        }

        public void SetFaulty()
        {
            state = TrackCircuitState.Faulty;
            OnCircuitFault?.Invoke(circuitId);
        }
    }
}
