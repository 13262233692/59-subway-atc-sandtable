using System;
using System.Collections.Generic;
using UnityEngine;
using CBTC.Sandtable.Track;

namespace CBTC.Sandtable.Signal
{
    public class InterlockingConflictCheck
    {
        public bool isClear = true;
        public bool isNodeLocked;
        public string lockingTrainId;
        public List<string> lockedNodes = new List<string>();
        public bool isOpposingRoute;
        public string conflictingRouteId;
        public List<string> conflictingSegments = new List<string>();
        public bool isTrackCircuitOccupied;
        public bool isSegmentConflicting;
    }

    public class NodeLockInfo
    {
        public string nodeId;
        public string lockedByTrainId;
        public string lockedByRouteId;
        public float lockTime;
        public bool isSwitchLocked;
    }

    public class ActiveRoute
    {
        public string routeId;
        public string trainId;
        public string startNodeId;
        public string endNodeId;
        public List<string> nodes = new List<string>();
        public List<string> segments = new List<string>();
        public float establishedTime;
        public bool isActive = true;
    }

    public class InterlockingController : MonoBehaviour
    {
        [SerializeField] private TrackNetwork _trackNetwork;
        [SerializeField] private CBTCZoneController _zoneController;
        [SerializeField] private InterlockingTable _interlockingTable;

        private AStarPathfinder _pathfinder;
        private Dictionary<string, NodeLockInfo> _nodeLocks = new Dictionary<string, NodeLockInfo>();
        private Dictionary<string, ActiveRoute> _activeRoutes = new Dictionary<string, ActiveRoute>();
        private Dictionary<string, string> _segmentToRouteMap = new Dictionary<string, string>();
        private Dictionary<string, string> _nodeToRouteMap = new Dictionary<string, string>();
        private List<InterlockingAlert> _alertHistory = new List<InterlockingAlert>();
        private int _routeIdCounter;
        private float _lastConflictFlashTime;
        private List<string> _flashingConflictNodes = new List<string>();
        private List<string> _flashingConflictSegments = new List<string>();

        public TrackNetwork Network => _trackNetwork;
        public InterlockingTable Table => _interlockingTable;
        public Dictionary<string, NodeLockInfo> NodeLocks => _nodeLocks;
        public Dictionary<string, ActiveRoute> ActiveRoutes => _activeRoutes;
        public List<string> FlashingConflictNodes => _flashingConflictNodes;
        public List<string> FlashingConflictSegments => _flashingConflictSegments;

        public event Action<InterlockingPathResult> OnRouteRequested;
        public event Action<string> OnRouteEstablished;
        public event Action<string> OnRouteReleased;
        public event Action<InterlockingAlert> OnInterlockingAlert;

        public void Initialize(TrackNetwork network, CBTCZoneController zoneController)
        {
            _trackNetwork = network;
            _zoneController = zoneController;
            _interlockingTable = new InterlockingTable();
            _pathfinder = new AStarPathfinder(_trackNetwork, this);
        }

        public InterlockingPathResult RequestRoute(string startNodeId, string endNodeId, string trainId = null)
        {
            InterlockingPathResult result;

            if (_pathfinder == null)
            {
                _pathfinder = new AStarPathfinder(_trackNetwork, this);
            }

            result = _pathfinder.FindPath(startNodeId, endNodeId, trainId);

            if (result.IsSuccess)
            {
                string routeId = GenerateRouteId();
                ActiveRoute route = new ActiveRoute
                {
                    routeId = routeId,
                    trainId = trainId ?? string.Empty,
                    startNodeId = startNodeId,
                    endNodeId = endNodeId,
                    nodes = new List<string>(result.nodes),
                    segments = new List<string>(result.segments),
                    establishedTime = Time.time,
                    isActive = true
                };

                _activeRoutes[routeId] = route;

                LockNodesForRoute(route);
                MapSegmentsForRoute(route);

                _interlockingTable.AddRoute(new RouteEntry(
                    routeId, startNodeId, endNodeId,
                    result.segments.ToArray(),
                    FindConflictingRouteIds(route).ToArray()));

                _interlockingTable.RequestRoute(routeId);

                OnRouteEstablished?.Invoke(routeId);
            }
            else
            {
                TriggerConflictFlash(result);

                InterlockingAlert alert = new InterlockingAlert
                {
                    alertType = result.resultType.ToString(),
                    startNodeId = startNodeId,
                    endNodeId = endNodeId,
                    trainId = trainId,
                    conflictNodeId = result.conflictNodeId,
                    conflictSegmentId = result.conflictSegmentId,
                    conflictingRouteId = result.conflictingRouteId,
                    lockedByTrainId = result.lockedByTrainId,
                    message = BuildConflictMessage(result, startNodeId, endNodeId),
                    timestamp = Time.time,
                    severity = AlertSeverity.Critical
                };
                _alertHistory.Add(alert);
                OnInterlockingAlert?.Invoke(alert);
            }

            OnRouteRequested?.Invoke(result);
            return result;
        }

        public void ReleaseRoute(string routeId)
        {
            if (!_activeRoutes.TryGetValue(routeId, out ActiveRoute route)) return;

            UnlockNodesForRoute(route);
            UnmapSegmentsForRoute(route);

            _interlockingTable.ReleaseRoute(routeId);
            _interlockingTable.RemoveRoute(routeId);

            route.isActive = false;
            _activeRoutes.Remove(routeId);

            OnRouteReleased?.Invoke(routeId);
        }

        public void ReleaseRoutesForTrain(string trainId)
        {
            List<string> toRelease = new List<string>();
            foreach (var kvp in _activeRoutes)
            {
                if (kvp.Value.trainId == trainId)
                    toRelease.Add(kvp.Key);
            }
            for (int i = 0; i < toRelease.Count; i++)
            {
                ReleaseRoute(toRelease[i]);
            }
        }

        public void LockNode(string nodeId, string trainId, string routeId)
        {
            if (_nodeLocks.ContainsKey(nodeId)) return;

            TrackNode node = _trackNetwork != null ? _trackNetwork.GetNode(nodeId) : null;
            _nodeLocks[nodeId] = new NodeLockInfo
            {
                nodeId = nodeId,
                lockedByTrainId = trainId,
                lockedByRouteId = routeId,
                lockTime = Time.time,
                isSwitchLocked = node != null && node.IsSwitchable()
            };

            _nodeToRouteMap[nodeId] = routeId;
        }

        public void UnlockNode(string nodeId)
        {
            _nodeLocks.Remove(nodeId);
            _nodeToRouteMap.Remove(nodeId);
        }

        public bool IsNodeLocked(string nodeId)
        {
            return _nodeLocks.ContainsKey(nodeId);
        }

        public NodeLockInfo GetNodeLockInfo(string nodeId)
        {
            _nodeLocks.TryGetValue(nodeId, out NodeLockInfo info);
            return info;
        }

        public bool IsSegmentOccupiedByActiveRoute(string segmentId, string excludeRouteId = null)
        {
            return _segmentToRouteMap.TryGetValue(segmentId, out string routeId) && routeId != excludeRouteId;
        }

        public InterlockingConflictCheck CheckNodeAndSegment(string nodeId, string segmentId, string requestingTrainId = null)
        {
            InterlockingConflictCheck check = new InterlockingConflictCheck();

            if (_nodeLocks.TryGetValue(nodeId, out NodeLockInfo lockInfo))
            {
                if (lockInfo.lockedByTrainId != requestingTrainId)
                {
                    check.isClear = false;
                    check.isNodeLocked = true;
                    check.lockingTrainId = lockInfo.lockedByTrainId;
                    check.lockedNodes.Add(nodeId);
                    return check;
                }
            }

            if (_segmentToRouteMap.TryGetValue(segmentId, out string owningRouteId))
            {
                ActiveRoute owningRoute = null;
                _activeRoutes.TryGetValue(owningRouteId, out owningRoute);

                if (owningRoute != null && owningRoute.trainId != requestingTrainId)
                {
                    check.isClear = false;
                    check.isSegmentConflicting = true;
                    check.conflictingSegments.Add(segmentId);

                    if (IsOpposingDirection(owningRoute, segmentId))
                    {
                        check.isOpposingRoute = true;
                        check.conflictingRouteId = owningRouteId;
                    }
                    return check;
                }
            }

            List<string> conflictingRouteIds = FindConflictingRoutesForSegment(segmentId);
            for (int i = 0; i < conflictingRouteIds.Count; i++)
            {
                string cRouteId = conflictingRouteIds[i];
                if (_activeRoutes.ContainsKey(cRouteId))
                {
                    ActiveRoute cRoute = _activeRoutes[cRouteId];
                    if (cRoute.trainId != requestingTrainId)
                    {
                        check.isClear = false;
                        check.isOpposingRoute = true;
                        check.conflictingRouteId = cRouteId;
                        check.conflictingSegments.Add(segmentId);
                        return check;
                    }
                }
            }

            if (_zoneController != null)
            {
                for (int i = 0; i < _zoneController.TrackCircuits.Count; i++)
                {
                    TrackCircuit tc = _zoneController.TrackCircuits[i];
                    if (tc.segmentId == segmentId && tc.state == TrackCircuitState.Occupied)
                    {
                        if (tc.occupyingTrainId != requestingTrainId)
                        {
                            check.isClear = false;
                            check.isTrackCircuitOccupied = true;
                            return check;
                        }
                    }
                }
            }

            return check;
        }

        public bool IsOpposingDirection(ActiveRoute route, string segmentId)
        {
            if (route == null) return false;

            for (int i = 0; i < route.segments.Count; i++)
            {
                if (route.segments[i] == segmentId)
                {
                    TrackNode nodeBefore = null;
                    if (i < route.nodes.Count - 1)
                    {
                        nodeBefore = _trackNetwork != null ? _trackNetwork.GetNode(route.nodes[i]) : null;
                    }

                    TrackSegment seg = _trackNetwork != null ? _trackNetwork.GetSegment(segmentId) : null;
                    if (seg == null || nodeBefore == null) return false;

                    ConnectionDirection dir = nodeBefore.GetDirection(segmentId);
                    return dir == ConnectionDirection.Reverse;
                }
            }
            return false;
        }

        public InterlockingPathResult FindPathOnly(string startNodeId, string endNodeId, string requestingTrainId = null)
        {
            if (_pathfinder == null)
            {
                _pathfinder = new AStarPathfinder(_trackNetwork, this);
            }
            return _pathfinder.FindPath(startNodeId, endNodeId, requestingTrainId);
        }

        public List<InterlockingAlert> GetAlertHistory()
        {
            return new List<InterlockingAlert>(_alertHistory);
        }

        public void ClearAlertHistory()
        {
            _alertHistory.Clear();
        }

        public void UpdateConflictFlash()
        {
            if (_flashingConflictNodes.Count > 0 || _flashingConflictSegments.Count > 0)
            {
                if (Time.time - _lastConflictFlashTime > 3f)
                {
                    _flashingConflictNodes.Clear();
                    _flashingConflictSegments.Clear();
                }
            }
        }

        private void LockNodesForRoute(ActiveRoute route)
        {
            for (int i = 0; i < route.nodes.Count; i++)
            {
                string nodeId = route.nodes[i];
                TrackNode node = _trackNetwork != null ? _trackNetwork.GetNode(nodeId) : null;
                if (node != null && (node.IsSwitchable() || node.Type == NodeType.Terminal || node.Type == NodeType.Station))
                {
                    LockNode(nodeId, route.trainId, route.routeId);
                }
            }
        }

        private void UnlockNodesForRoute(ActiveRoute route)
        {
            for (int i = 0; i < route.nodes.Count; i++)
            {
                if (_nodeToRouteMap.TryGetValue(route.nodes[i], out string rid) && rid == route.routeId)
                {
                    UnlockNode(route.nodes[i]);
                }
            }
        }

        private void MapSegmentsForRoute(ActiveRoute route)
        {
            for (int i = 0; i < route.segments.Count; i++)
            {
                _segmentToRouteMap[route.segments[i]] = route.routeId;
            }
        }

        private void UnmapSegmentsForRoute(ActiveRoute route)
        {
            for (int i = 0; i < route.segments.Count; i++)
            {
                if (_segmentToRouteMap.TryGetValue(route.segments[i], out string rid) && rid == route.routeId)
                {
                    _segmentToRouteMap.Remove(route.segments[i]);
                }
            }
        }

        private List<string> FindConflictingRouteIds(ActiveRoute route)
        {
            List<string> conflicts = new List<string>();

            foreach (var kvp in _activeRoutes)
            {
                if (kvp.Key == route.routeId) continue;

                ActiveRoute other = kvp.Value;
                bool hasOverlap = false;

                for (int i = 0; i < route.segments.Count; i++)
                {
                    for (int j = 0; j < other.segments.Count; j++)
                    {
                        if (route.segments[i] == other.segments[j])
                        {
                            hasOverlap = true;
                            break;
                        }
                    }
                    if (hasOverlap) break;
                }

                if (!hasOverlap)
                {
                    for (int i = 0; i < route.nodes.Count; i++)
                    {
                        TrackNode node = _trackNetwork != null ? _trackNetwork.GetNode(route.nodes[i]) : null;
                        if (node != null && node.IsSwitchable())
                        {
                            for (int j = 0; j < other.nodes.Count; j++)
                            {
                                if (route.nodes[i] == other.nodes[j])
                                {
                                    hasOverlap = true;
                                    break;
                                }
                            }
                        }
                        if (hasOverlap) break;
                    }
                }

                if (hasOverlap)
                {
                    conflicts.Add(kvp.Key);
                }
            }

            return conflicts;
        }

        private List<string> FindConflictingRoutesForSegment(string segmentId)
        {
            List<string> conflicts = new List<string>();
            foreach (var kvp in _activeRoutes)
            {
                for (int i = 0; i < kvp.Value.segments.Count; i++)
                {
                    if (kvp.Value.segments[i] == segmentId)
                    {
                        conflicts.Add(kvp.Key);
                        break;
                    }
                }
            }
            return conflicts;
        }

        private void TriggerConflictFlash(InterlockingPathResult result)
        {
            _flashingConflictNodes.Clear();
            _flashingConflictSegments.Clear();

            if (!string.IsNullOrEmpty(result.conflictNodeId))
            {
                _flashingConflictNodes.Add(result.conflictNodeId);
            }

            if (!string.IsNullOrEmpty(result.conflictSegmentId))
            {
                _flashingConflictSegments.Add(result.conflictSegmentId);
            }

            for (int i = 0; i < result.lockedNodes.Count; i++)
            {
                if (!_flashingConflictNodes.Contains(result.lockedNodes[i]))
                    _flashingConflictNodes.Add(result.lockedNodes[i]);
            }

            for (int i = 0; i < result.conflictingSegments.Count; i++)
            {
                if (!_flashingConflictSegments.Contains(result.conflictingSegments[i]))
                    _flashingConflictSegments.Add(result.conflictingSegments[i]);
            }

            if (!string.IsNullOrEmpty(result.conflictingRouteId) && _activeRoutes.TryGetValue(result.conflictingRouteId, out ActiveRoute conflictRoute))
            {
                for (int i = 0; i < conflictRoute.nodes.Count; i++)
                {
                    TrackNode node = _trackNetwork != null ? _trackNetwork.GetNode(conflictRoute.nodes[i]) : null;
                    if (node != null && node.IsSwitchable() && !_flashingConflictNodes.Contains(conflictRoute.nodes[i]))
                    {
                        _flashingConflictNodes.Add(conflictRoute.nodes[i]);
                    }
                }

                for (int i = 0; i < conflictRoute.segments.Count; i++)
                {
                    if (!_flashingConflictSegments.Contains(conflictRoute.segments[i]))
                        _flashingConflictSegments.Add(conflictRoute.segments[i]);
                }
            }

            _lastConflictFlashTime = Time.time;
        }

        private string GenerateRouteId()
        {
            _routeIdCounter++;
            return $"RT_{_routeIdCounter:D4}_{Time.time:F0}";
        }

        private string BuildConflictMessage(InterlockingPathResult result, string startNodeId, string endNodeId)
        {
            switch (result.resultType)
            {
                case PathfindingResultType.NodeLocked:
                    return $"ROUTE REJECTED: Node {result.conflictNodeId} locked by train {result.lockedByTrainId}. Path {startNodeId}→{endNodeId} blocked.";

                case PathfindingResultType.OpposingRouteConflict:
                    return $"ROUTE REJECTED: Opposing route {result.conflictingRouteId} conflicts at segment {result.conflictSegmentId}. Path {startNodeId}→{endNodeId} is hostile.";

                case PathfindingResultType.TrackCircuitOccupied:
                    return $"ROUTE REJECTED: Track circuit occupied at segment {result.conflictSegmentId}. Path {startNodeId}→{endNodeId} blocked.";

                case PathfindingResultType.SegmentConflict:
                    return $"ROUTE REJECTED: Segment {result.conflictSegmentId} already allocated. Path {startNodeId}→{endNodeId} blocked.";

                case PathfindingResultType.NoPath:
                    return $"ROUTE REJECTED: No valid path from {startNodeId} to {endNodeId}.";

                default:
                    return $"Route request from {startNodeId} to {endNodeId} failed.";
            }
        }
    }

    [Serializable]
    public struct InterlockingAlert
    {
        public string alertType;
        public string startNodeId;
        public string endNodeId;
        public string trainId;
        public string conflictNodeId;
        public string conflictSegmentId;
        public string conflictingRouteId;
        public string lockedByTrainId;
        public string message;
        public float timestamp;
        public AlertSeverity severity;
    }
}
