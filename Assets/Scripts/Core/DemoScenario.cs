using System.Collections.Generic;
using UnityEngine;
using CBTC.Sandtable.Track;
using CBTC.Sandtable.Train;
using CBTC.Sandtable.Signal;

namespace CBTC.Sandtable.Core
{
    public class DemoScenario : MonoBehaviour
    {
        [SerializeField] private SandtableController _controller;
        [SerializeField] private int _trainCount = 4;
        [SerializeField] private float _lineSpeed = 22.22f;
        [SerializeField] private float _headway = 90f;
        [SerializeField] private float _stationSpacing = 1500f;
        [SerializeField] private int _stationCount = 8;
        [SerializeField] private bool _autoStart = true;
        [SerializeField] private float _trackLength = 12000f;

        private bool _scenarioStarted;
        private float _demoTime;
        private List<string> _stationNodeIds = new List<string>();
        private List<string> _segmentIds = new List<string>();
        private string _routeFromNodeId = "";
        private string _routeToNodeId = "";
        private string _lastRouteResult = "";
        private string _lastRouteId = "";
        private int _selectedFromIdx;
        private int _selectedToIdx;

        public string ScenarioInfo =>
            $"Trains: {_trainCount}, Speed: {_lineSpeed:F1} m/s, Headway: {_headway:F0}s, Stations: {_stationCount}";

        private void Start()
        {
            if (_autoStart)
                StartScenario();
        }

        private void Update()
        {
            if (!_scenarioStarted) return;

            _demoTime += Time.deltaTime;

            UpdateDemoDisplay();
        }

        public void StartScenario()
        {
            if (_scenarioStarted) return;

            _controller = _controller ?? FindObjectOfType<SandtableController>();
            if (_controller == null)
            {
                Debug.LogError("SandtableController not found");
                return;
            }

            _controller.Initialize();

            BuildDemoTrackNetwork();
            BuildDemoTrains();

            _scenarioStarted = true;
            _demoTime = 0f;
        }

        public void StopScenario()
        {
            if (!_scenarioStarted) return;

            var infos = _controller.GetAllTrainInfo();
            for (int i = 0; i < infos.Count; i++)
            {
                _controller.RemoveTrain(infos[i].trainId);
            }

            _scenarioStarted = false;
        }

        private void BuildDemoTrackNetwork()
        {
            TrackNetwork network = _controller.TrackNetwork;
            if (network == null) return;

            _stationNodeIds.Clear();
            _segmentIds.Clear();

            GameObject networkObj = network.gameObject;

            for (int i = 0; i <= _stationCount; i++)
            {
                string nodeId = i == 0 ? "Terminal_A" : i == _stationCount ? "Terminal_B" : $"Station_{i}";
                NodeType nodeType = (i == 0 || i == _stationCount) ? NodeType.Terminal : NodeType.Station;

                GameObject nodeObj = new GameObject($"Node_{nodeId}");
                nodeObj.transform.SetParent(networkObj.transform);
                TrackNode node = nodeObj.AddComponent<TrackNode>();
                Vector3 nodePos = new Vector3(i * _stationSpacing, 0f, 0f);
                node.Initialize(nodeId, nodeId.Replace("_", " "), nodePos, nodeType);
                network.RegisterNode(node);
                _stationNodeIds.Add(nodeId);
            }

            for (int i = 0; i < _stationNodeIds.Count - 1; i++)
            {
                string segId = $"Seg_{_stationNodeIds[i]}_{_stationNodeIds[i + 1]}";

                GameObject segObj = new GameObject($"Segment_{segId}");
                segObj.transform.SetParent(networkObj.transform);

                CubicBezierSpline spline = segObj.AddComponent<CubicBezierSpline>();

                Vector3 start = network.GetNode(_stationNodeIds[i]).Position;
                Vector3 end = network.GetNode(_stationNodeIds[i + 1]).Position;
                float segLen = Vector3.Distance(start, end);

                BuildSegmentSpline(spline, start, end, segLen, i);

                TrackSegment segment = segObj.AddComponent<TrackSegment>();
                segment.Initialize(segId, "DemoLine1", spline);
                segment.SetEndpoints(_stationNodeIds[i], _stationNodeIds[i + 1]);
                segment.SetProperties(_lineSpeed, 0f, float.MaxValue);

                BuildSegmentSections(segment, segLen);

                network.RegisterSegment(segment);
                _segmentIds.Add(segId);

                network.GetNode(_stationNodeIds[i]).AddConnection(segId, ConnectionDirection.Forward);
                network.GetNode(_stationNodeIds[i + 1]).AddConnection(segId, ConnectionDirection.Reverse);
            }

            network.ValidateNetwork();
        }

        private void BuildSegmentSpline(CubicBezierSpline spline, Vector3 start, Vector3 end, float length, int segIndex)
        {
            spline.AddPoint(start);

            float midDist = length * 0.33f;
            float midDist2 = length * 0.67f;

            Vector3 dir = (end - start).normalized;

            float lateralOffset = Mathf.Sin(segIndex * 1.5f) * 80f;
            Vector3 lateral = Vector3.Cross(dir, Vector3.up).normalized * lateralOffset;

            Vector3 cp1 = start + dir * midDist + lateral + Vector3.up * GetElevation(segIndex, midDist);
            Vector3 cp2 = start + dir * midDist2 - lateral * 0.7f + Vector3.up * GetElevation(segIndex, midDist2);

            spline.AddPoint(cp1);
            spline.AddPoint(cp2);
            spline.AddPoint(end);

            spline.Recalculate();
        }

        private float GetElevation(int segIndex, float distance)
        {
            return Mathf.Sin(segIndex * 0.5f + distance * 0.002f) * 8f;
        }

        private void BuildSegmentSections(TrackSegment segment, float segLen)
        {
            float approachLen = 100f;
            float stationLen = 80f;

            segment.AddSection(new TrackSection
            {
                startDistance = 0f,
                endDistance = approachLen,
                speedLimit = _lineSpeed * 0.6f,
                sectionType = SectionType.Station
            });

            segment.AddSection(new TrackSection
            {
                startDistance = approachLen,
                endDistance = segLen - approachLen,
                speedLimit = _lineSpeed,
                sectionType = SectionType.Mainline
            });

            segment.AddSection(new TrackSection
            {
                startDistance = segLen - approachLen,
                endDistance = segLen,
                speedLimit = _lineSpeed * 0.6f,
                sectionType = SectionType.Station
            });
        }

        private void BuildDemoTrains()
        {
            if (_segmentIds.Count == 0) return;

            string firstSegId = _segmentIds[0];
            float spacing = _lineSpeed * _headway;

            for (int i = 0; i < _trainCount; i++)
            {
                string trainId = $"Train_{i + 1:D3}";
                float startDist = i * spacing;

                int segIdx = 0;
                float remaining = startDist;
                TrackSegment seg = _controller.TrackNetwork.GetSegment(_segmentIds[0]);
                while (seg != null && remaining > seg.Length && segIdx < _segmentIds.Count - 1)
                {
                    remaining -= seg.Length;
                    segIdx++;
                    seg = _controller.TrackNetwork.GetSegment(_segmentIds[segIdx]);
                }

                string spawnSegId = _segmentIds[Mathf.Min(segIdx, _segmentIds.Count - 1)];
                float spawnDist = Mathf.Clamp(remaining, 0f, seg != null ? seg.Length - 10f : 0f);

                _controller.SpawnTrain(trainId, spawnSegId, spawnDist, 0f);

                _controller.DispatchTrain(trainId, _stationNodeIds[_stationNodeIds.Count - 1], _lineSpeed);
            }
        }

        private void UpdateDemoDisplay()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _controller.IsPaused = !_controller.IsPaused;
            }

            if (Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                _controller.SimulationTimeScale = Mathf.Min(_controller.SimulationTimeScale * 2f, 16f);
            }

            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                _controller.SimulationTimeScale = Mathf.Max(_controller.SimulationTimeScale * 0.5f, 0.25f);
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                var infos = _controller.GetAllTrainInfo();
                if (infos.Count > 0)
                {
                    _controller.EmergencyStopTrain(infos[0].trainId);
                }
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                StopScenario();
                StartScenario();
            }
        }

        private void OnGUI()
        {
            if (!_scenarioStarted) return;

            GUILayout.BeginArea(new Rect(10, 10, 440, 650));
            GUILayout.BeginVertical("box");

            GUILayout.Label("<b>CBTC Subway ATC Sandtable</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 });
            GUILayout.Space(5);

            GUILayout.Label($"Sim Time: {_demoTime:F1}s  |  Scale: {_controller.SimulationTimeScale:F1}x  |  {(_controller.IsPaused ? "PAUSED" : "RUNNING")}");

            GUILayout.Space(5);
            GUILayout.Label("<b>Train Fleet</b>", new GUIStyle(GUI.skin.label) { richText = true });

            var infos = _controller.GetAllTrainInfo();
            for (int i = 0; i < infos.Count; i++)
            {
                var info = infos[i];
                float speedKmh = info.speed * 3.6f;
                GUILayout.Label($"  {info.trainId}: {speedKmh:F1} km/h | {info.movementState} | ATO:{info.atoState} | Seg:{info.segmentId}");
            }

            GUILayout.Space(5);
            var status = _controller.GetSystemStatus();
            GUILayout.Label($"System: {status.activeTrains} active / {status.totalTrains} total | Circuits: {status.freeCircuits} free / {status.occupiedCircuits} occupied");

            if (status.alerts != null && status.alerts.Count > 0)
            {
                GUILayout.Space(3);
                GUILayout.Label("<b>Alerts:</b>", new GUIStyle(GUI.skin.label) { richText = true });
                int showCount = Mathf.Min(status.alerts.Count, 3);
                for (int i = status.alerts.Count - 1; i >= status.alerts.Count - showCount; i--)
                {
                    GUILayout.Label($"  [{status.alerts[i].severity}] {status.alerts[i].message}");
                }
            }

            GUILayout.Space(8);
            DrawInterlockingPanel();

            GUILayout.Space(8);
            GUILayout.Label("[Space] Pause/Resume  [+/-] Time Scale  [E] Emergency  [R] Restart");

            GUILayout.EndVertical();
            GUILayout.EndArea();

            if (!string.IsNullOrEmpty(_lastRouteResult) && _controller.Interlocking != null)
            {
                GUILayout.BeginArea(new Rect(460, 10, 400, 300));
                GUILayout.BeginVertical("box");
                GUILayout.Label("<b>Interlocking Console</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 });
                GUILayout.Space(4);

                var alerts = _controller.GetInterlockingAlerts();
                int alertShow = Mathf.Min(alerts.Count, 5);
                for (int i = alerts.Count - 1; i >= alerts.Count - alertShow; i--)
                {
                    GUI.color = alerts[i].severity == AlertSeverity.Critical ? Color.red :
                                alerts[i].severity == AlertSeverity.Emergency ? new Color(1f, 0.3f, 0f) :
                                Color.yellow;
                    GUILayout.Label(alerts[i].message);
                }
                GUI.color = Color.white;

                GUILayout.Space(4);
                GUILayout.Label(_lastRouteResult);

                if (!string.IsNullOrEmpty(_lastRouteId) && GUILayout.Button("Release Last Route"))
                {
                    _controller.ReleaseInterlockingRoute(_lastRouteId);
                    _lastRouteId = "";
                }

                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
        }

        private void DrawInterlockingPanel()
        {
            GUILayout.Label("<b>Interlocking Route Request</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13 });
            GUILayout.Space(3);

            if (_stationNodeIds.Count < 2) return;

            GUILayout.BeginHorizontal();
            GUILayout.Label("From:", GUILayout.Width(40));
            if (GUILayout.Button(_stationNodeIds[_selectedFromIdx], GUILayout.Width(120)))
            {
                _selectedFromIdx = (_selectedFromIdx + 1) % _stationNodeIds.Count;
            }
            GUILayout.Label("To:", GUILayout.Width(25));
            if (GUILayout.Button(_stationNodeIds[_selectedToIdx], GUILayout.Width(120)))
            {
                _selectedToIdx = (_selectedToIdx + 1) % _stationNodeIds.Count;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(3);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Request Route", GUILayout.Height(28)))
            {
                if (_selectedFromIdx != _selectedToIdx)
                {
                    string fromId = _stationNodeIds[_selectedFromIdx];
                    string toId = _stationNodeIds[_selectedToIdx];
                    InterlockingPathResult result = _controller.RequestInterlockingRoute(fromId, toId);
                    if (result != null)
                    {
                        if (result.IsSuccess)
                        {
                            _lastRouteId = _controller.Interlocking.ActiveRoutes.Count > 0 ?
                                GetLatestRouteId() : "";
                            _lastRouteResult = $"<color=green>ROUTE ESTABLISHED: {fromId} → {toId}\nDistance: {result.totalDistance:F0}m | Segments: {result.segments.Count}</color>";
                        }
                        else
                        {
                            _lastRouteResult = $"<color=red>ROUTE REJECTED: {result.resultType}\n{BuildResultDetail(result)}</color>";
                        }
                    }
                }
            }

            if (GUILayout.Button("Preview", GUILayout.Height(28)))
            {
                if (_selectedFromIdx != _selectedToIdx)
                {
                    string fromId = _stationNodeIds[_selectedFromIdx];
                    string toId = _stationNodeIds[_selectedToIdx];
                    InterlockingPathResult result = _controller.PreviewRoute(fromId, toId);
                    if (result != null)
                    {
                        _lastRouteResult = result.IsSuccess
                            ? $"PREVIEW OK: {fromId} → {toId} | {result.totalDistance:F0}m | {result.segments.Count} segs"
                            : $"PREVIEW FAIL: {result.resultType}";
                    }
                }
            }

            GUILayout.EndHorizontal();

            if (_controller.Interlocking != null)
            {
                GUILayout.Space(3);
                int activeRoutes = _controller.Interlocking.ActiveRoutes.Count;
                int lockedNodes = _controller.Interlocking.NodeLocks.Count;
                GUILayout.Label($"Active Routes: {activeRoutes} | Locked Nodes: {lockedNodes}");
            }
        }

        private string GetLatestRouteId()
        {
            string latest = "";
            float latestTime = 0f;
            foreach (var kvp in _controller.Interlocking.ActiveRoutes)
            {
                if (kvp.Value.establishedTime > latestTime)
                {
                    latestTime = kvp.Value.establishedTime;
                    latest = kvp.Key;
                }
            }
            return latest;
        }

        private string BuildResultDetail(InterlockingPathResult result)
        {
            switch (result.resultType)
            {
                case PathfindingResultType.NodeLocked:
                    return $"Node {result.conflictNodeId} LOCKED by train {result.lockedByTrainId}";
                case PathfindingResultType.OpposingRouteConflict:
                    return $"OPPOSING route {result.conflictingRouteId} at seg {result.conflictSegmentId}";
                case PathfindingResultType.TrackCircuitOccupied:
                    return $"Track circuit OCCUPIED at seg {result.conflictSegmentId}";
                case PathfindingResultType.SegmentConflict:
                    return $"Segment {result.conflictSegmentId} ALREADY ALLOCATED";
                case PathfindingResultType.NoPath:
                    return "No valid path found";
                default:
                    return result.resultType.ToString();
            }
        }
    }
}
