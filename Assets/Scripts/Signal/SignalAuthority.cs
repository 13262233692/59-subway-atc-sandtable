using System;
using System.Collections.Generic;

namespace CBTC.Sandtable.Signal
{
    public enum SignalAuthorityType
    {
        FullAuthority,
        RestrictedAuthority,
        StopAuthority,
        EmergencyStop
    }

    public struct SignalAuthority
    {
        public string authorityId;
        public string trainId;
        public string endSegmentId;
        public float endDistance;
        public float maxSpeed;
        public SignalAuthorityType authorityType;
        public float issuedTime;
        public float expiryTime;

        public SignalAuthority(string authorityId, string trainId, string endSegmentId, float endDistance,
            float maxSpeed, SignalAuthorityType authorityType, float issuedTime, float expiryTime)
        {
            this.authorityId = authorityId;
            this.trainId = trainId;
            this.endSegmentId = endSegmentId;
            this.endDistance = endDistance;
            this.maxSpeed = maxSpeed;
            this.authorityType = authorityType;
            this.issuedTime = issuedTime;
            this.expiryTime = expiryTime;
        }
    }

    public class SignalAuthorityManager
    {
        private Dictionary<string, SignalAuthority> _activeAuthorities = new Dictionary<string, SignalAuthority>();
        private List<TrackCircuit> _trackCircuits = new List<TrackCircuit>();

        public Dictionary<string, SignalAuthority> ActiveAuthorities => _activeAuthorities;

        public void SetTrackCircuits(List<TrackCircuit> circuits)
        {
            _trackCircuits = circuits;
        }

        public void IssueAuthority(string trainId, SignalAuthority authority)
        {
            _activeAuthorities[trainId] = authority;
        }

        public void RevokeAuthority(string trainId)
        {
            _activeAuthorities.Remove(trainId);
        }

        public void ExtendAuthority(string trainId, float newEndDistance, string newEndSegmentId)
        {
            if (!_activeAuthorities.TryGetValue(trainId, out SignalAuthority authority)) return;

            authority.endDistance = newEndDistance;
            authority.endSegmentId = newEndSegmentId;
            _activeAuthorities[trainId] = authority;
        }

        public SignalAuthority? GetAuthority(string trainId)
        {
            if (_activeAuthorities.TryGetValue(trainId, out SignalAuthority authority))
            {
                return authority;
            }
            return null;
        }

        public bool ValidateAuthority(string trainId)
        {
            if (!_activeAuthorities.TryGetValue(trainId, out SignalAuthority authority)) return false;

            if (authority.expiryTime > 0f && authority.expiryTime < UnityEngine.Time.time)
            {
                RevokeAuthority(trainId);
                return false;
            }

            if (authority.authorityType == SignalAuthorityType.EmergencyStop) return false;

            for (int i = 0; i < _trackCircuits.Count; i++)
            {
                TrackCircuit circuit = _trackCircuits[i];
                if (circuit.segmentId != authority.endSegmentId) continue;
                if (circuit.state == TrackCircuitState.Occupied && circuit.occupyingTrainId != trainId)
                {
                    return false;
                }
                if (circuit.state == TrackCircuitState.Faulty)
                {
                    return false;
                }
            }

            return true;
        }

        public void UpdateAuthorities()
        {
            List<string> trainsToRevoke = new List<string>();

            foreach (var kvp in _activeAuthorities)
            {
                string trainId = kvp.Key;
                SignalAuthority authority = kvp.Value;

                if (authority.expiryTime > 0f && authority.expiryTime < UnityEngine.Time.time)
                {
                    trainsToRevoke.Add(trainId);
                    continue;
                }

                if (authority.authorityType == SignalAuthorityType.EmergencyStop) continue;

                bool blocked = false;
                for (int i = 0; i < _trackCircuits.Count; i++)
                {
                    TrackCircuit circuit = _trackCircuits[i];
                    if (circuit.segmentId != authority.endSegmentId) continue;
                    if (circuit.state == TrackCircuitState.Occupied && circuit.occupyingTrainId != trainId)
                    {
                        blocked = true;
                        break;
                    }
                    if (circuit.state == TrackCircuitState.Faulty)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (blocked)
                {
                    authority.authorityType = SignalAuthorityType.StopAuthority;
                    authority.maxSpeed = 0f;
                    _activeAuthorities[trainId] = authority;
                }
            }

            for (int i = 0; i < trainsToRevoke.Count; i++)
            {
                RevokeAuthority(trainsToRevoke[i]);
            }
        }
    }
}
