using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using CBTC.Sandtable.Track;

namespace CBTC.Sandtable.Editor
{
    [CustomEditor(typeof(TrackNetwork))]
    public class TrackNetworkEditor : UnityEditor.Editor
    {
        private string _fromNodeId = "";
        private string _toNodeId = "";
        private Vector2 _scrollPos;
        private bool _showNodes = true;
        private bool _showSegments = true;
        private PathResult _lastPathResult;

        public override void OnInspectorGUI()
        {
            TrackNetwork network = (TrackNetwork)target;

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Track Network Editor", EditorStyles.boldLabel);

            EditorGUILayout.Space(5);
            DrawActionButtons(network);

            EditorGUILayout.Space(10);
            DrawNetworkStatistics(network);

            EditorGUILayout.Space(10);
            DrawFindPathSection(network);

            EditorGUILayout.Space(10);
            DrawNodeList(network);

            EditorGUILayout.Space(5);
            DrawSegmentList(network);

            if (GUI.changed)
            {
                EditorUtility.SetDirty(network);
            }
        }

        private void DrawActionButtons(TrackNetwork network)
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate Network", GUILayout.Height(30)))
            {
                network.Initialize();
                bool valid = network.ValidateNetwork();
                if (valid)
                {
                    EditorUtility.DisplayDialog("Validation Result", "Network validation passed! All connections are valid.", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Validation Result", "Network validation failed! Check the console for details.", "OK");
                }
            }

            if (GUILayout.Button("Rebuild Index", GUILayout.Height(30)))
            {
                Undo.RecordObject(network, "Rebuild Network Index");
                network.Initialize();
                EditorUtility.SetDirty(network);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Auto Generate Track Circuits", GUILayout.Height(30)))
            {
                AutoGenerateTrackCircuits(network);
            }
        }

        private void DrawNetworkStatistics(TrackNetwork network)
        {
            EditorGUILayout.LabelField("Network Statistics", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Nodes:", network.NodeCount.ToString());
            EditorGUILayout.LabelField("Segments:", network.SegmentCount.ToString());

            float totalLength = 0f;
            int totalSections = 0;
            foreach (var kvp in network.Segments)
            {
                TrackSegment seg = kvp.Value;
                if (seg != null)
                {
                    totalLength += seg.Length;
                    totalSections += seg.SectionCount;
                }
            }

            EditorGUILayout.LabelField("Total Length:", totalLength.ToString("F1") + " m");
            EditorGUILayout.LabelField("Total Sections:", totalSections.ToString());

            int stationCount = 0;
            int turnoutCount = 0;
            int terminalCount = 0;
            foreach (var kvp in network.Nodes)
            {
                TrackNode node = kvp.Value;
                if (node != null)
                {
                    switch (node.Type)
                    {
                        case NodeType.Station: stationCount++; break;
                        case NodeType.Turnout: turnoutCount++; break;
                        case NodeType.Terminal: terminalCount++; break;
                    }
                }
            }

            EditorGUILayout.LabelField("Stations:", stationCount.ToString());
            EditorGUILayout.LabelField("Turnouts:", turnoutCount.ToString());
            EditorGUILayout.LabelField("Terminals:", terminalCount.ToString());

            EditorGUI.indentLevel--;
        }

        private void DrawFindPathSection(TrackNetwork network)
        {
            EditorGUILayout.LabelField("Find Path", EditorStyles.boldLabel);

            _fromNodeId = EditorGUILayout.TextField("From Node:", _fromNodeId);
            _toNodeId = EditorGUILayout.TextField("To Node:", _toNodeId);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Find Path", GUILayout.Height(25)))
            {
                if (string.IsNullOrEmpty(_fromNodeId) || string.IsNullOrEmpty(_toNodeId))
                {
                    EditorUtility.DisplayDialog("Find Path", "Please enter both From and To node IDs.", "OK");
                }
                else
                {
                    _lastPathResult = network.FindPath(_fromNodeId, _toNodeId);
                    if (_lastPathResult != null && _lastPathResult.IsValid)
                    {
                        Debug.Log($"Path found: {string.Join(" -> ", _lastPathResult.nodes.ToArray())}, Distance: {_lastPathResult.totalDistance:F1}m");
                    }
                    else
                    {
                        Debug.LogWarning($"No path found from {_fromNodeId} to {_toNodeId}");
                    }
                }
            }

            if (GUILayout.Button("Clear", GUILayout.Height(25)))
            {
                _lastPathResult = null;
                _fromNodeId = "";
                _toNodeId = "";
            }
            EditorGUILayout.EndHorizontal();

            if (_lastPathResult != null && _lastPathResult.IsValid)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Distance:", _lastPathResult.totalDistance.ToString("F1") + " m");
                EditorGUILayout.LabelField("Nodes:", string.Join(" → ", _lastPathResult.nodes.ToArray()));
                EditorGUILayout.LabelField("Segments:", string.Join(", ", _lastPathResult.segments.ToArray()));
                EditorGUI.indentLevel--;
            }
        }

        private void DrawNodeList(TrackNetwork network)
        {
            _showNodes = EditorGUILayout.Foldout(_showNodes, $"Nodes ({network.NodeCount})");
            if (!_showNodes) return;

            EditorGUI.indentLevel++;
            foreach (var kvp in network.Nodes)
            {
                TrackNode node = kvp.Value;
                if (node == null) continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{kvp.Key}]", GUILayout.Width(120));
                EditorGUILayout.LabelField(node.NodeName, GUILayout.Width(100));
                EditorGUILayout.LabelField(node.Type.ToString(), GUILayout.Width(70));
                EditorGUILayout.LabelField($"Conns: {node.ConnectionCount}", GUILayout.Width(60));

                if (GUILayout.Button("Select", GUILayout.Width(50)))
                {
                    Selection.activeGameObject = node.gameObject;
                    SceneView.Frame(new Bounds(node.Position, Vector3.one * 10f), false);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        private void DrawSegmentList(TrackNetwork network)
        {
            _showSegments = EditorGUILayout.Foldout(_showSegments, $"Segments ({network.SegmentCount})");
            if (!_showSegments) return;

            EditorGUI.indentLevel++;
            foreach (var kvp in network.Segments)
            {
                TrackSegment seg = kvp.Value;
                if (seg == null) continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{kvp.Key}]", GUILayout.Width(120));
                EditorGUILayout.LabelField($"{seg.StartStation} → {seg.EndStation}", GUILayout.Width(150));
                EditorGUILayout.LabelField($"{seg.Length:F0}m", GUILayout.Width(60));
                EditorGUILayout.LabelField($"Limit: {seg.SpeedLimit * 3.6f:F0}km/h", GUILayout.Width(90));

                if (GUILayout.Button("Select", GUILayout.Width(50)))
                {
                    Selection.activeGameObject = seg.gameObject;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        private void AutoGenerateTrackCircuits(TrackNetwork network)
        {
            network.Initialize();
            int generated = 0;
            float circuitLength = 200f;

            foreach (var kvp in network.Segments)
            {
                TrackSegment seg = kvp.Value;
                if (seg == null) continue;

                float remaining = seg.Length;
                float startDist = 0f;
                int circuitIndex = 0;

                while (remaining > 0f)
                {
                    float endDist = startDist + Mathf.Min(circuitLength, remaining);

                    TrackSection section = new TrackSection
                    {
                        startDistance = startDist,
                        endDistance = endDist,
                        speedLimit = seg.SpeedLimit,
                        sectionType = SectionType.Mainline
                    };
                    seg.AddSection(section);

                    startDist = endDist;
                    remaining -= circuitLength;
                    circuitIndex++;
                    generated++;
                }
            }

            Debug.Log($"Auto generated {generated} track circuit sections across {network.SegmentCount} segments.");
            EditorUtility.SetDirty(network);
        }
    }
}
