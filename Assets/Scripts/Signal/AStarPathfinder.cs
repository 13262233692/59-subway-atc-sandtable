using System.Collections.Generic;
using UnityEngine;

namespace CBTC.Sandtable.Signal
{
    public enum PathfindingResultType
    {
        Success,
        NoPath,
        NodeLocked,
        OpposingRouteConflict,
        TrackCircuitOccupied,
        SegmentConflict
    }

    public class AStarNode
    {
        public string nodeId;
        public string viaSegmentId;
        public float gCost;
        public float hCost;
        public float fCost;
        public AStarNode parent;

        public AStarNode(string nodeId, string viaSegmentId)
        {
            this.nodeId = nodeId;
            this.viaSegmentId = viaSegmentId;
            gCost = 0f;
            hCost = 0f;
            fCost = 0f;
            parent = null;
        }
    }

    public class InterlockingPathResult
    {
        public PathfindingResultType resultType;
        public List<string> nodes = new List<string>();
        public List<string> segments = new List<string>();
        public float totalDistance;
        public string conflictNodeId;
        public string conflictSegmentId;
        public string conflictingRouteId;
        public string lockedByTrainId;
        public List<string> lockedNodes = new List<string>();
        public List<string> conflictingSegments = new List<string>();

        public bool IsSuccess => resultType == PathfindingResultType.Success;
    }

    public class AStarPathfinder
    {
        private TrackNetwork _network;
        private InterlockingController _interlocking;

        public AStarPathfinder(TrackNetwork network, InterlockingController interlocking)
        {
            _network = network;
            _interlocking = interlocking;
        }

        public InterlockingPathResult FindPath(string startNodeId, string endNodeId, string requestingTrainId = null)
        {
            InterlockingPathResult result = new InterlockingPathResult();

            if (_network == null)
            {
                result.resultType = PathfindingResultType.NoPath;
                return result;
            }

            TrackNode startNode = _network.GetNode(startNodeId);
            TrackNode endNode = _network.GetNode(endNodeId);

            if (startNode == null || endNode == null)
            {
                result.resultType = PathfindingResultType.NoPath;
                return result;
            }

            Dictionary<string, AStarNode> openMap = new Dictionary<string, AStarNode>();
            HashSet<string> closedSet = new HashSet<string>();
            List<AStarNode> openList = new List<AStarNode>();

            AStarNode startAStar = new AStarNode(startNodeId, null);
            startAStar.gCost = 0f;
            startAStar.hCost = CalculateHeuristic(startNode.Position, endNode.Position);
            startAStar.fCost = startAStar.gCost + startAStar.hCost;

            openList.Add(startAStar);
            openMap[startNodeId] = startAStar;

            int maxIterations = _network.NodeCount * 4;
            int iteration = 0;

            while (openList.Count > 0 && iteration < maxIterations)
            {
                iteration++;

                int bestIdx = 0;
                float bestFCost = openList[0].fCost;
                for (int i = 1; i < openList.Count; i++)
                {
                    if (openList[i].fCost < bestFCost ||
                        (openList[i].fCost == bestFCost && openList[i].hCost < openList[bestIdx].hCost))
                    {
                        bestFCost = openList[i].fCost;
                        bestIdx = i;
                    }
                }

                AStarNode current = openList[bestIdx];
                openList.RemoveAt(bestIdx);
                openMap.Remove(current.nodeId);

                if (current.nodeId == endNodeId)
                {
                    return BuildPath(current, result);
                }

                closedSet.Add(current.nodeId);

                TrackNode currentTrackNode = _network.GetNode(current.nodeId);
                if (currentTrackNode == null) continue;

                for (int i = 0; i < currentTrackNode.Connections.Count; i++)
                {
                    TrackConnection conn = currentTrackNode.Connections[i];
                    TrackSegment seg = _network.GetSegment(conn.segmentId);
                    if (seg == null) continue;

                    string neighborNodeId = conn.direction == ConnectionDirection.Forward
                        ? seg.EndStation
                        : seg.StartStation;

                    if (string.IsNullOrEmpty(neighborNodeId)) continue;
                    if (closedSet.Contains(neighborNodeId)) continue;
                    if (neighborNodeId == current.nodeId) continue;

                    if (conn.segmentId == current.viaSegmentId && current.parent != null) continue;

                    InterlockingConflictCheck conflictCheck = _interlocking.CheckNodeAndSegment(
                        neighborNodeId, conn.segmentId, requestingTrainId);

                    if (!conflictCheck.isClear)
                    {
                        if (conflictCheck.isNodeLocked)
                        {
                            result.resultType = PathfindingResultType.NodeLocked;
                            result.conflictNodeId = neighborNodeId;
                            result.lockedByTrainId = conflictCheck.lockingTrainId;
                            result.lockedNodes = conflictCheck.lockedNodes;
                            return result;
                        }

                        if (conflictCheck.isOpposingRoute)
                        {
                            result.resultType = PathfindingResultType.OpposingRouteConflict;
                            result.conflictNodeId = neighborNodeId;
                            result.conflictSegmentId = conn.segmentId;
                            result.conflictingRouteId = conflictCheck.conflictingRouteId;
                            result.conflictingSegments = conflictCheck.conflictingSegments;
                            return result;
                        }

                        if (conflictCheck.isTrackCircuitOccupied)
                        {
                            result.resultType = PathfindingResultType.TrackCircuitOccupied;
                            result.conflictSegmentId = conn.segmentId;
                            return result;
                        }

                        if (conflictCheck.isSegmentConflicting)
                        {
                            result.resultType = PathfindingResultType.SegmentConflict;
                            result.conflictSegmentId = conn.segmentId;
                            result.conflictingSegments = conflictCheck.conflictingSegments;
                            return result;
                        }

                        continue;
                    }

                    float tentativeG = current.gCost + seg.Length;

                    AStarNode neighborAStar;
                    if (openMap.TryGetValue(neighborNodeId, out neighborAStar))
                    {
                        if (tentativeG < neighborAStar.gCost)
                        {
                            neighborAStar.gCost = tentativeG;
                            neighborAStar.hCost = CalculateHeuristic(
                                _network.GetNode(neighborNodeId).Position, endNode.Position);
                            neighborAStar.fCost = neighborAStar.gCost + neighborAStar.hCost;
                            neighborAStar.parent = current;
                            neighborAStar.viaSegmentId = conn.segmentId;
                        }
                    }
                    else
                    {
                        neighborAStar = new AStarNode(neighborNodeId, conn.segmentId);
                        neighborAStar.gCost = tentativeG;
                        neighborAStar.hCost = CalculateHeuristic(
                            _network.GetNode(neighborNodeId).Position, endNode.Position);
                        neighborAStar.fCost = neighborAStar.gCost + neighborAStar.hCost;
                        neighborAStar.parent = current;

                        openList.Add(neighborAStar);
                        openMap[neighborNodeId] = neighborAStar;
                    }
                }
            }

            result.resultType = PathfindingResultType.NoPath;
            return result;
        }

        private float CalculateHeuristic(Vector3 from, Vector3 to)
        {
            return Vector3.Distance(from, to);
        }

        private InterlockingPathResult BuildPath(AStarNode endNode, InterlockingPathResult result)
        {
            List<string> pathNodes = new List<string>();
            List<string> pathSegments = new List<string>();

            AStarNode current = endNode;
            while (current != null)
            {
                pathNodes.Add(current.nodeId);
                if (current.viaSegmentId != null)
                    pathSegments.Add(current.viaSegmentId);
                current = current.parent;
            }

            pathNodes.Reverse();
            pathSegments.Reverse();

            result.resultType = PathfindingResultType.Success;
            result.nodes = pathNodes;
            result.segments = pathSegments;

            result.totalDistance = 0f;
            for (int i = 0; i < pathSegments.Count; i++)
            {
                TrackSegment seg = _network.GetSegment(pathSegments[i]);
                if (seg != null)
                    result.totalDistance += seg.Length;
            }

            return result;
        }
    }
}
