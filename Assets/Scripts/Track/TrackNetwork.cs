using System;
using System.Collections.Generic;
using UnityEngine;

namespace CBTC.Sandtable.Track
{
    [Serializable]
    public class PathResult
    {
        public List<string> nodes = new List<string>();
        public List<string> segments = new List<string>();
        public float totalDistance;

        public bool IsValid => nodes.Count > 0 && segments.Count > 0;
    }

    public class TrackNetwork : MonoBehaviour
    {
        [SerializeField] private List<TrackSegment> _segmentList = new List<TrackSegment>();
        [SerializeField] private List<TrackNode> _nodeList = new List<TrackNode>();

        private Dictionary<string, TrackSegment> _segmentIndex = new Dictionary<string, TrackSegment>();
        private Dictionary<string, TrackNode> _nodeIndex = new Dictionary<string, TrackNode>();
        private bool _initialized = false;

        public Dictionary<string, TrackSegment> Segments => _segmentIndex;
        public Dictionary<string, TrackNode> Nodes => _nodeIndex;
        public int SegmentCount => _segmentIndex.Count;
        public int NodeCount => _nodeIndex.Count;

        public void Initialize()
        {
            _segmentIndex.Clear();
            _nodeIndex.Clear();

            for (int i = 0; i < _segmentList.Count; i++)
            {
                TrackSegment seg = _segmentList[i];
                if (seg != null && !string.IsNullOrEmpty(seg.SegmentId))
                {
                    if (!_segmentIndex.ContainsKey(seg.SegmentId))
                        _segmentIndex[seg.SegmentId] = seg;
                }
            }

            for (int i = 0; i < _nodeList.Count; i++)
            {
                TrackNode node = _nodeList[i];
                if (node != null && !string.IsNullOrEmpty(node.NodeId))
                {
                    if (!_nodeIndex.ContainsKey(node.NodeId))
                        _nodeIndex[node.NodeId] = node;
                }
            }

            _initialized = true;
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
                Initialize();
        }

        public void RegisterSegment(TrackSegment segment)
        {
            if (segment == null || string.IsNullOrEmpty(segment.SegmentId)) return;
            _segmentIndex[segment.SegmentId] = segment;
            if (!_segmentList.Contains(segment))
                _segmentList.Add(segment);
        }

        public void UnregisterSegment(string segmentId)
        {
            _segmentIndex.Remove(segmentId);
            _segmentList.RemoveAll(s => s != null && s.SegmentId == segmentId);
        }

        public void RegisterNode(TrackNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.NodeId)) return;
            _nodeIndex[node.NodeId] = node;
            if (!_nodeList.Contains(node))
                _nodeList.Add(node);
        }

        public void UnregisterNode(string nodeId)
        {
            _nodeIndex.Remove(nodeId);
            _nodeList.RemoveAll(n => n != null && n.NodeId == nodeId);
        }

        public TrackSegment GetSegment(string segmentId)
        {
            EnsureInitialized();
            _segmentIndex.TryGetValue(segmentId, out TrackSegment seg);
            return seg;
        }

        public TrackNode GetNode(string nodeId)
        {
            EnsureInitialized();
            _nodeIndex.TryGetValue(nodeId, out TrackNode node);
            return node;
        }

        public TrackSegment GetSegmentAtDistance(string segmentId, float distance)
        {
            EnsureInitialized();
            _segmentIndex.TryGetValue(segmentId, out TrackSegment seg);
            return seg;
        }

        public Vector3 GetPositionAtDistance(string segmentId, float distance)
        {
            TrackSegment seg = GetSegment(segmentId);
            if (seg == null) return Vector3.zero;
            return seg.GetWorldPosition(distance);
        }

        public Vector3 GetTangentAtDistance(string segmentId, float distance)
        {
            TrackSegment seg = GetSegment(segmentId);
            if (seg == null) return Vector3.forward;
            return seg.GetWorldTangent(distance);
        }

        public Vector3 GetNormalAtDistance(string segmentId, float distance)
        {
            TrackSegment seg = GetSegment(segmentId);
            if (seg == null) return Vector3.up;
            return seg.GetWorldNormal(distance);
        }

        public PathResult FindPath(string fromNodeId, string toNodeId)
        {
            EnsureInitialized();
            PathResult result = new PathResult();

            if (!_nodeIndex.ContainsKey(fromNodeId) || !_nodeIndex.ContainsKey(toNodeId))
                return result;

            if (fromNodeId == toNodeId)
            {
                result.nodes.Add(fromNodeId);
                return result;
            }

            Dictionary<string, float> dist = new Dictionary<string, float>();
            Dictionary<string, string> prev = new Dictionary<string, string>();
            Dictionary<string, string> prevSegment = new Dictionary<string, string>();
            HashSet<string> visited = new HashSet<string>();
            List<string> queue = new List<string>();

            foreach (var kvp in _nodeIndex)
            {
                dist[kvp.Key] = float.MaxValue;
            }
            dist[fromNodeId] = 0f;
            queue.Add(fromNodeId);

            while (queue.Count > 0)
            {
                float minDist = float.MaxValue;
                int minIdx = -1;
                for (int i = 0; i < queue.Count; i++)
                {
                    if (dist[queue[i]] < minDist)
                    {
                        minDist = dist[queue[i]];
                        minIdx = i;
                    }
                }

                if (minIdx < 0) break;

                string current = queue[minIdx];
                queue.RemoveAt(minIdx);

                if (visited.Contains(current)) continue;
                visited.Add(current);

                if (current == toNodeId) break;

                TrackNode currentNode = _nodeIndex[current];
                if (currentNode == null) continue;

                for (int i = 0; i < currentNode.Connections.Count; i++)
                {
                    TrackConnection conn = currentNode.Connections[i];
                    TrackSegment seg = GetSegment(conn.segmentId);
                    if (seg == null) continue;

                    string neighborId = conn.direction == ConnectionDirection.Forward
                        ? seg.EndStation
                        : seg.StartStation;

                    if (string.IsNullOrEmpty(neighborId) || !_nodeIndex.ContainsKey(neighborId)) continue;
                    if (visited.Contains(neighborId)) continue;

                    float alt = dist[current] + seg.Length;
                    if (alt < dist[neighborId])
                    {
                        dist[neighborId] = alt;
                        prev[neighborId] = current;
                        prevSegment[neighborId] = conn.segmentId;
                        queue.Add(neighborId);
                    }
                }
            }

            if (!dist.ContainsKey(toNodeId) || dist[toNodeId] >= float.MaxValue)
                return result;

            List<string> pathNodes = new List<string>();
            List<string> pathSegments = new List<string>();
            string curr = toNodeId;

            while (curr != null)
            {
                pathNodes.Add(curr);
                if (prevSegment.ContainsKey(curr))
                    pathSegments.Add(prevSegment[curr]);
                curr = prev.ContainsKey(curr) ? prev[curr] : null;
            }

            pathNodes.Reverse();
            pathSegments.Reverse();

            result.nodes = pathNodes;
            result.segments = pathSegments;
            result.totalDistance = dist[toNodeId];

            return result;
        }

        public string GetNextSegment(string currentSegmentId, string currentNodeId, string nextNodeId)
        {
            EnsureInitialized();
            TrackNode nextNode = GetNode(nextNodeId);
            if (nextNode == null) return null;

            TrackNode currentNode = GetNode(currentNodeId);
            if (currentNode == null) return null;

            for (int i = 0; i < nextNode.Connections.Count; i++)
            {
                TrackConnection conn = nextNode.Connections[i];
                if (conn.segmentId == currentSegmentId) continue;
                return conn.segmentId;
            }

            return null;
        }

        public bool ValidateNetwork()
        {
            EnsureInitialized();
            bool valid = true;

            foreach (var kvp in _nodeIndex)
            {
                TrackNode node = kvp.Value;
                if (node == null) continue;

                for (int i = 0; i < node.Connections.Count; i++)
                {
                    TrackConnection conn = node.Connections[i];
                    if (!_segmentIndex.ContainsKey(conn.segmentId))
                    {
                        Debug.LogError($"Node {kvp.Key} references missing segment {conn.segmentId}");
                        valid = false;
                    }
                    else
                    {
                        TrackSegment seg = _segmentIndex[conn.segmentId];
                        bool nodeBelongsToSegment = false;

                        if (seg.StartStation == kvp.Key || seg.EndStation == kvp.Key)
                            nodeBelongsToSegment = true;

                        if (!nodeBelongsToSegment)
                        {
                            Debug.LogWarning($"Node {kvp.Key} connects to segment {conn.segmentId} but is not its start/end station");
                        }
                    }
                }
            }

            foreach (var kvp in _segmentIndex)
            {
                TrackSegment seg = kvp.Value;
                if (seg == null) continue;

                if (!_nodeIndex.ContainsKey(seg.StartStation) && !string.IsNullOrEmpty(seg.StartStation))
                {
                    Debug.LogError($"Segment {kvp.Key} references missing start node {seg.StartStation}");
                    valid = false;
                }

                if (!_nodeIndex.ContainsKey(seg.EndStation) && !string.IsNullOrEmpty(seg.EndStation))
                {
                    Debug.LogError($"Segment {kvp.Key} references missing end node {seg.EndStation}");
                    valid = false;
                }

                if (seg.Spline == null)
                {
                    Debug.LogWarning($"Segment {kvp.Key} has no spline reference");
                }
            }

            HashSet<string> visited = new HashSet<string>();
            List<string> stack = new List<string>();

            if (_nodeIndex.Count > 0)
            {
                var enumerator = _nodeIndex.GetEnumerator();
                if (enumerator.MoveNext())
                    stack.Add(enumerator.Current.Key);
                enumerator.Dispose();
            }

            while (stack.Count > 0)
            {
                string nodeId = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);

                if (visited.Contains(nodeId)) continue;
                visited.Add(nodeId);

                TrackNode node = _nodeIndex[nodeId];
                if (node == null) continue;

                for (int i = 0; i < node.Connections.Count; i++)
                {
                    TrackConnection conn = node.Connections[i];
                    TrackSegment seg = GetSegment(conn.segmentId);
                    if (seg == null) continue;

                    string neighbor = conn.direction == ConnectionDirection.Forward
                        ? seg.EndStation
                        : seg.StartStation;

                    if (!string.IsNullOrEmpty(neighbor) && !visited.Contains(neighbor))
                        stack.Add(neighbor);
                }
            }

            if (visited.Count < _nodeIndex.Count)
            {
                Debug.LogError($"Network is not fully connected. {visited.Count}/{_nodeIndex.Count} nodes reachable.");
                valid = false;
            }

            return valid;
        }

        public List<TrackNode> GetNeighborNodes(string nodeId)
        {
            EnsureInitialized();
            List<TrackNode> neighbors = new List<TrackNode>();
            TrackNode node = GetNode(nodeId);
            if (node == null) return neighbors;

            for (int i = 0; i < node.Connections.Count; i++)
            {
                TrackConnection conn = node.Connections[i];
                TrackSegment seg = GetSegment(conn.segmentId);
                if (seg == null) continue;

                string neighborId = conn.direction == ConnectionDirection.Forward
                    ? seg.EndStation
                    : seg.StartStation;

                TrackNode neighbor = GetNode(neighborId);
                if (neighbor != null && !neighbors.Contains(neighbor))
                    neighbors.Add(neighbor);
            }

            return neighbors;
        }

        public List<TrackSegment> GetSegmentsForNode(string nodeId)
        {
            EnsureInitialized();
            List<TrackSegment> result = new List<TrackSegment>();
            TrackNode node = GetNode(nodeId);
            if (node == null) return result;

            for (int i = 0; i < node.Connections.Count; i++)
            {
                TrackSegment seg = GetSegment(node.Connections[i].segmentId);
                if (seg != null)
                    result.Add(seg);
            }

            return result;
        }

        public TrackSegment FindNearestSegment(Vector3 worldPos)
        {
            EnsureInitialized();
            float minDist = float.MaxValue;
            TrackSegment nearest = null;

            foreach (var kvp in _segmentIndex)
            {
                TrackSegment seg = kvp.Value;
                if (seg == null || seg.Spline == null) continue;

                Vector3 pt = seg.Spline.GetNearestPoint(worldPos);
                float dist = Vector3.Distance(worldPos, pt);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = seg;
                }
            }

            return nearest;
        }
    }
}
