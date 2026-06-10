using System;
using System.Collections.Generic;
using UnityEngine;

namespace CBTC.Sandtable.Track
{
    public enum NodeType
    {
        Station,
        Turnout,
        Terminal,
        Crossover
    }

    public enum ConnectionDirection
    {
        Forward,
        Reverse
    }

    [Serializable]
    public struct TrackConnection
    {
        public string segmentId;
        public ConnectionDirection direction;

        public TrackConnection(string segId, ConnectionDirection dir)
        {
            segmentId = segId;
            direction = dir;
        }
    }

    public class TrackNode : MonoBehaviour
    {
        [SerializeField] private string _nodeId;
        [SerializeField] private string _nodeName;
        [SerializeField] private Vector3 _position;
        [SerializeField] private NodeType _nodeType;
        [SerializeField] private List<TrackConnection> _connections = new List<TrackConnection>();
        [SerializeField] private bool _isLocked;
        [SerializeField] private string _lockedByTrainId;
        [SerializeField] private string _lockedByRouteId;
        [SerializeField] private float _lockTime;

        public string NodeId => _nodeId;
        public string NodeName => _nodeName;
        public Vector3 Position => _position;
        public NodeType Type => _nodeType;
        public List<TrackConnection> Connections => _connections;
        public int ConnectionCount => _connections.Count;
        public bool IsLocked => _isLocked;
        public string LockedByTrainId => _lockedByTrainId;
        public string LockedByRouteId => _lockedByRouteId;
        public float LockTime => _lockTime;

        public void Initialize(string nodeId, string nodeName, Vector3 position, NodeType nodeType)
        {
            _nodeId = nodeId;
            _nodeName = nodeName;
            _position = position;
            _nodeType = nodeType;
            transform.position = position;
        }

        public void AddConnection(string segmentId, ConnectionDirection direction)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                if (_connections[i].segmentId == segmentId)
                    return;
            }
            _connections.Add(new TrackConnection(segmentId, direction));
        }

        public void RemoveConnection(string segmentId)
        {
            _connections.RemoveAll(c => c.segmentId == segmentId);
        }

        public bool HasConnection(string segmentId)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                if (_connections[i].segmentId == segmentId)
                    return true;
            }
            return false;
        }

        public ConnectionDirection GetDirection(string segmentId)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                if (_connections[i].segmentId == segmentId)
                    return _connections[i].direction;
            }
            return ConnectionDirection.Forward;
        }

        public bool IsSwitchable()
        {
            return _nodeType == NodeType.Turnout || _nodeType == NodeType.Crossover;
        }

        public void SetLocked(bool locked, string trainId = null, string routeId = null)
        {
            _isLocked = locked;
            _lockedByTrainId = locked ? trainId : null;
            _lockedByRouteId = locked ? routeId : null;
            _lockTime = locked ? Time.time : 0f;
        }

        public void ForceUnlock()
        {
            _isLocked = false;
            _lockedByTrainId = null;
            _lockedByRouteId = null;
            _lockTime = 0f;
        }
    }
}
