using System.Collections.Generic;
using UnityEngine;
using CBTC.Sandtable.Track;
using CBTC.Sandtable.Signal;

namespace CBTC.Sandtable.Visualization
{
    public class InterlockingVisualizer : MonoBehaviour
    {
        [SerializeField] private InterlockingController _interlocking;
        [SerializeField] private TrackNetwork _trackNetwork;
        [SerializeField] private float _nodeGizmoSize = 3f;
        [SerializeField] private float _segmentLineWidth = 0.8f;
        [SerializeField] private float _flashFrequency = 4f;
        [SerializeField] private float _conflictDisplayDuration = 5f;
        [SerializeField] private bool _showActiveRoutes = true;
        [SerializeField] private bool _showLockedNodes = true;
        [SerializeField] private bool _showConflictFlash = true;

        private float _conflictFlashTimer;
        private List<GameObject> _conflictMarkers = new List<GameObject>();
        private List<GameObject> _routeMarkers = new List<GameObject>();

        public bool ShowActiveRoutes { get => _showActiveRoutes; set => _showActiveRoutes = value; }
        public bool ShowLockedNodes { get => _showLockedNodes; set => _showLockedNodes = value; }
        public bool ShowConflictFlash { get => _showConflictFlash; set => _showConflictFlash = value; }

        private void Update()
        {
            if (_interlocking != null)
                _interlocking.UpdateConflictFlash();

            UpdateConflictMarkers();
        }

        private void OnDrawGizmos()
        {
            if (_interlocking == null || _trackNetwork == null) return;

            if (_showLockedNodes)
                DrawLockedNodes();

            if (_showActiveRoutes)
                DrawActiveRoutes();

            if (_showConflictFlash)
                DrawConflictFlash();
        }

        private void DrawLockedNodes()
        {
            Dictionary<string, NodeLockInfo> locks = _interlocking.NodeLocks;
            foreach (var kvp in locks)
            {
                TrackNode node = _trackNetwork.GetNode(kvp.Key);
                if (node == null) continue;

                Gizmos.color = kvp.Value.isSwitchLocked ? new Color(1f, 0.5f, 0f) : Color.yellow;

                Vector3 pos = node.Position + Vector3.up * 2f;
                Gizmos.DrawWireSphere(pos, _nodeGizmoSize);

                Gizmos.color = Color.red;
                Vector3 lockIcon = pos + Vector3.up * _nodeGizmoSize;
                Gizmos.DrawCube(lockIcon, Vector3.one * 1.5f);

                Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
                Gizmos.DrawSphere(pos, _nodeGizmoSize * 1.5f);
            }
        }

        private void DrawActiveRoutes()
        {
            Dictionary<string, ActiveRoute> routes = _interlocking.ActiveRoutes;
            int routeIdx = 0;

            foreach (var kvp in routes)
            {
                ActiveRoute route = kvp.Value;
                if (!route.isActive) continue;

                Color routeColor = GetRouteColor(routeIdx);
                Gizmos.color = routeColor;

                for (int i = 0; i < route.segments.Count; i++)
                {
                    TrackSegment seg = _trackNetwork.GetSegment(route.segments[i]);
                    if (seg == null || seg.Spline == null) continue;

                    int sampleCount = 60;
                    Vector3 prevPt = seg.Spline.GetPoint(0f);
                    for (int s = 1; s <= sampleCount; s++)
                    {
                        float t = (float)s / sampleCount;
                        Vector3 pt = seg.Spline.GetPoint(t);
                        Gizmos.DrawLine(prevPt + Vector3.up * 0.5f, pt + Vector3.up * 0.5f);
                        prevPt = pt;
                    }
                }

                for (int i = 0; i < route.nodes.Count; i++)
                {
                    TrackNode node = _trackNetwork.GetNode(route.nodes[i]);
                    if (node == null) continue;

                    Gizmos.color = routeColor;
                    Gizmos.DrawWireSphere(node.Position + Vector3.up * 1f, _nodeGizmoSize * 0.6f);
                }

                routeIdx++;
            }
        }

        private void DrawConflictFlash()
        {
            if (_interlocking.FlashingConflictNodes.Count == 0 &&
                _interlocking.FlashingConflictSegments.Count == 0) return;

            float flashPhase = Mathf.PingPong(Time.time * _flashFrequency, 1f);
            bool flashOn = flashPhase > 0.3f;

            if (!flashOn) return;

            Color flashColor = Color.Lerp(Color.red, new Color(1f, 0f, 0f, 0.4f), Mathf.PingPong(Time.time * 8f, 1f));
            Gizmos.color = flashColor;

            List<string> conflictNodes = _interlocking.FlashingConflictNodes;
            for (int i = 0; i < conflictNodes.Count; i++)
            {
                TrackNode node = _trackNetwork.GetNode(conflictNodes[i]);
                if (node == null) continue;

                Vector3 pos = node.Position + Vector3.up * 3f;

                Gizmos.DrawSphere(pos, _nodeGizmoSize * 2f);

                Gizmos.color = Color.white;
                Gizmos.DrawLine(pos, pos + Vector3.up * 4f);
                Gizmos.DrawLine(pos + Vector3.left * 2f + Vector3.up * 4f, pos + Vector3.right * 2f + Vector3.up * 4f);
                Gizmos.DrawLine(pos + Vector3.forward * 2f + Vector3.up * 4f, pos + Vector3.back * 2f + Vector3.up * 4f);
                Gizmos.DrawLine(pos + Vector3.up * 3f, pos + Vector3.up * 5f);

                Gizmos.color = flashColor;
            }

            Gizmos.color = Color.red;
            List<string> conflictSegments = _interlocking.FlashingConflictSegments;
            for (int i = 0; i < conflictSegments.Count; i++)
            {
                TrackSegment seg = _trackNetwork.GetSegment(conflictSegments[i]);
                if (seg == null || seg.Spline == null) continue;

                int sampleCount = 40;
                Vector3 prevPt = seg.Spline.GetPoint(0f);
                for (int s = 1; s <= sampleCount; s++)
                {
                    float t = (float)s / sampleCount;
                    Vector3 pt = seg.Spline.GetPoint(t);
                    Gizmos.DrawLine(prevPt + Vector3.up * 1.5f, pt + Vector3.up * 1.5f);
                    prevPt = pt;
                }
            }
        }

        private void UpdateConflictMarkers()
        {
            ClearConflictMarkers();

            if (!showConflictFlash) return;
            if (_interlocking == null || _trackNetwork == null) return;

            List<string> conflictNodes = _interlocking.FlashingConflictNodes;
            for (int i = 0; i < conflictNodes.Count; i++)
            {
                TrackNode node = _trackNetwork.GetNode(conflictNodes[i]);
                if (node == null) continue;

                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = $"ConflictMarker_{conflictNodes[i]}";
                marker.transform.position = node.Position + Vector3.up * 4f;
                marker.transform.localScale = Vector3.one * 3f;

                Renderer rend = marker.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.material = new Material(Shader.Find("Standard"));
                    rend.material.color = Color.red;
                    rend.material.SetFloat("_Mode", 3);
                    rend.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    rend.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    rend.material.SetInt("_ZWrite", 0);
                    rend.material.DisableKeyword("_ALPHATEST_ON");
                    rend.material.EnableKeyword("_ALPHABLEND_ON");
                    rend.material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    rend.material.renderQueue = 3000;

                    ConflictFlash flash = marker.AddComponent<ConflictFlash>();
                    flash.frequency = _flashFrequency;
                }

                Collider col = marker.GetComponent<Collider>();
                if (col != null) Destroy(col);

                _conflictMarkers.Add(marker);
            }

            List<string> conflictSegments = _interlocking.FlashingConflictSegments;
            for (int i = 0; i < conflictSegments.Count; i++)
            {
                TrackSegment seg = _trackNetwork.GetSegment(conflictSegments[i]);
                if (seg == null) continue;

                Vector3 midPos = seg.GetWorldPosition(seg.Length * 0.5f);

                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = $"ConflictSegMarker_{conflictSegments[i]}";
                marker.transform.position = midPos + Vector3.up * 3f;
                marker.transform.localScale = new Vector3(4f, 4f, 4f);

                Renderer rend = marker.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.material = new Material(Shader.Find("Standard"));
                    rend.material.color = new Color(1f, 0.2f, 0f);
                    rend.material.SetFloat("_Mode", 3);
                    rend.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    rend.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    rend.material.SetInt("_ZWrite", 0);
                    rend.material.DisableKeyword("_ALPHATEST_ON");
                    rend.material.EnableKeyword("_ALPHABLEND_ON");
                    rend.material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    rend.material.renderQueue = 3000;

                    ConflictFlash flash = marker.AddComponent<ConflictFlash>();
                    flash.frequency = _flashFrequency;
                }

                Collider col = marker.GetComponent<Collider>();
                if (col != null) Destroy(col);

                _conflictMarkers.Add(marker);
            }
        }

        private bool showConflictFlash => _showConflictFlash;

        private void ClearConflictMarkers()
        {
            for (int i = _conflictMarkers.Count - 1; i >= 0; i--)
            {
                if (_conflictMarkers[i] != null)
                    Destroy(_conflictMarkers[i]);
            }
            _conflictMarkers.Clear();
        }

        private Color GetRouteColor(int index)
        {
            Color[] palette = new Color[]
            {
                new Color(0f, 0.8f, 1f),
                new Color(0f, 1f, 0.5f),
                new Color(0.6f, 0.4f, 1f),
                new Color(1f, 0.8f, 0f),
                new Color(1f, 0.4f, 0.8f),
                new Color(0.4f, 1f, 0.8f),
                new Color(0.8f, 0.6f, 0.2f),
                new Color(0.2f, 0.6f, 1f)
            };
            return palette[index % palette.Length];
        }

        private void OnDestroy()
        {
            ClearConflictMarkers();
            for (int i = _routeMarkers.Count - 1; i >= 0; i--)
            {
                if (_routeMarkers[i] != null)
                    Destroy(_routeMarkers[i]);
            }
        }
    }

    public class ConflictFlash : MonoBehaviour
    {
        public float frequency = 4f;
        private Renderer _renderer;
        private Color _baseColor;

        private void Start()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer != null)
                _baseColor = _renderer.material.color;
        }

        private void Update()
        {
            if (_renderer == null) return;

            float phase = Mathf.PingPong(Time.time * frequency, 1f);
            bool flashOn = phase > 0.3f;

            if (flashOn)
            {
                float intensity = Mathf.PingPong(Time.time * 10f, 1f);
                _renderer.material.color = Color.Lerp(_baseColor, Color.white, intensity * 0.5f);
                transform.localScale = Vector3.one * (2.5f + intensity * 1.5f);
            }
            else
            {
                _renderer.material.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, 0.2f);
                transform.localScale = Vector3.one * 2f;
            }
        }
    }
}
