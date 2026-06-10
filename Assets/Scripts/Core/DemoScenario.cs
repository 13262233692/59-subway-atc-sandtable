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

            float lateralOffset = Mathf.Sin(segIndex * 1.5f) * 30f;
            Vector3 lateral = Vector3.Cross(dir, Vector3.up).normalized * lateralOffset;

            Vector3 cp1 = start + dir * midDist + lateral + Vector3.up * GetElevation(segIndex, midDist);
            Vector3 cp2 = start + dir * midDist2 - lateral * 0.5f + Vector3.up * GetElevation(segIndex, midDist2);

            spline.AddPoint(cp1);
            spline.AddPoint(cp2);
            spline.AddPoint(end);

            spline.Recalculate();
        }

        private float GetElevation(int segIndex, float distance)
        {
            return Mathf.Sin(segIndex * 0.5f + distance * 0.001f) * 3f;
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

            GUILayout.BeginArea(new Rect(10, 10, 420, 600));
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

            GUILayout.Space(10);
            GUILayout.Label("[Space] Pause/Resume  [+/-] Time Scale  [E] Emergency  [R] Restart");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}
