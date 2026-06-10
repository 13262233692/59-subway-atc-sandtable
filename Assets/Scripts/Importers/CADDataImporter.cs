using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CBTC.Sandtable.Track;
using CBTC.Sandtable.Signal;

namespace CBTC.Sandtable.Importers
{
    [Serializable]
    public struct CADPoint
    {
        public float x;
        public float y;
        public float z;
        public float station;

        public CADPoint(float x, float y, float z, float station)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.station = station;
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }

    [Serializable]
    public class CADAlignmentData
    {
        public string alignmentName;
        public List<CADPoint> points = new List<CADPoint>();
        public float startStation;
        public float endStation;
    }

    [Serializable]
    public class CADSpeedRestrictionData
    {
        public float startStation;
        public float endStation;
        public float speedLimit;
        public string restrictionType;
    }

    [Serializable]
    public class CADStationData
    {
        public string stationName;
        public float stationMileage;
        public float platformLength;
    }

    [Serializable]
    public class CADImportResult
    {
        public List<CADAlignmentData> alignments = new List<CADAlignmentData>();
        public List<CADSpeedRestrictionData> speedRestrictions = new List<CADSpeedRestrictionData>();
        public List<CADStationData> stations = new List<CADStationData>();
    }

    public static class CADDataImporter
    {
        public static CADImportResult ImportFromCSV(string filePath)
        {
            CADImportResult result = new CADImportResult();

            if (!File.Exists(filePath))
            {
                Debug.LogError($"CSV file not found: {filePath}");
                return result;
            }

            CADAlignmentData alignment = new CADAlignmentData();
            alignment.alignmentName = Path.GetFileNameWithoutExtension(filePath);

            string[] lines = File.ReadAllLines(filePath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (i == 0 && IsHeaderLine(line)) continue;

                string[] parts = line.Split(',');
                if (parts.Length < 4) continue;

                if (!float.TryParse(parts[0], out float station)) continue;
                if (!float.TryParse(parts[1], out float easting)) continue;
                if (!float.TryParse(parts[2], out float northing)) continue;
                if (!float.TryParse(parts[3], out float elevation)) continue;

                CADPoint point = new CADPoint(easting, elevation, northing, station);
                alignment.points.Add(point);
            }

            if (alignment.points.Count > 0)
            {
                alignment.startStation = alignment.points[0].station;
                alignment.endStation = alignment.points[alignment.points.Count - 1].station;
                result.alignments.Add(alignment);
            }

            return result;
        }

        public static CADImportResult ImportFromJSON(string filePath)
        {
            CADImportResult result = new CADImportResult();

            if (!File.Exists(filePath))
            {
                Debug.LogError($"JSON file not found: {filePath}");
                return result;
            }

            string json = File.ReadAllText(filePath);
            try
            {
                result = JsonUtility.FromJson<CADImportResult>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse JSON: {e.Message}");
            }

            return result;
        }

        public static SplineControlPoint[] ConvertToSplineControlPoints(CADAlignmentData data, float smoothFactor = 0.3f)
        {
            if (data == null || data.points == null || data.points.Count < 2)
                return Array.Empty<SplineControlPoint>();

            int count = data.points.Count;
            SplineControlPoint[] controlPoints = new SplineControlPoint[count];

            for (int i = 0; i < count; i++)
            {
                controlPoints[i] = new SplineControlPoint(data.points[i].ToVector3());
            }

            for (int i = 0; i < count; i++)
            {
                Vector3 pos = controlPoints[i].position;

                if (i == 0)
                {
                    Vector3 next = controlPoints[1].position;
                    float dist = Vector3.Distance(pos, next);
                    controlPoints[i].outTangent = pos + (next - pos).normalized * dist * smoothFactor;
                    controlPoints[i].inTangent = pos;
                }
                else if (i == count - 1)
                {
                    Vector3 prev = controlPoints[i - 1].position;
                    float dist = Vector3.Distance(pos, prev);
                    controlPoints[i].inTangent = pos - (pos - prev).normalized * dist * smoothFactor;
                    controlPoints[i].outTangent = pos;
                }
                else
                {
                    Vector3 prev = controlPoints[i - 1].position;
                    Vector3 next = controlPoints[i + 1].position;
                    Vector3 dir = (next - prev).normalized;
                    float dist = Mathf.Min(Vector3.Distance(prev, pos), Vector3.Distance(pos, next));
                    controlPoints[i].inTangent = pos - dir * dist * smoothFactor;
                    controlPoints[i].outTangent = pos + dir * dist * smoothFactor;
                }
            }

            return controlPoints;
        }

        public static TrackSegment GenerateTrackSegment(CADAlignmentData alignment, CADSpeedRestrictionData[] speedRestrictions)
        {
            if (alignment == null || alignment.points == null || alignment.points.Count < 2)
                return null;

            GameObject segmentObj = new GameObject($"Segment_{alignment.alignmentName}");
            GameObject splineObj = new GameObject("Spline");
            splineObj.transform.SetParent(segmentObj.transform);

            CubicBezierSpline spline = splineObj.AddComponent<CubicBezierSpline>();

            SplineControlPoint[] controlPoints = ConvertToSplineControlPoints(alignment);
            for (int i = 0; i < controlPoints.Length; i++)
            {
                spline.AddPoint(controlPoints[i].position);
            }
            spline.Recalculate();

            TrackSegment segment = segmentObj.AddComponent<TrackSegment>();
            segment.Initialize($"seg_{alignment.alignmentName}", "", spline);
            segment.SetEndpoints("", "");

            float minRadius = float.MaxValue;
            for (int i = 1; i < alignment.points.Count - 1; i++)
            {
                float radius = CalculateCurveRadiusFromPoints(
                    alignment.points[i - 1].ToVector3(),
                    alignment.points[i].ToVector3(),
                    alignment.points[i + 1].ToVector3());
                if (radius > 0f && radius < minRadius)
                    minRadius = radius;
            }

            float curveRadius = minRadius < float.MaxValue ? minRadius : 0f;
            float speedLimit = 80f / 3.6f;

            if (speedRestrictions != null && speedRestrictions.Length > 0)
            {
                float minLimit = float.MaxValue;
                for (int i = 0; i < speedRestrictions.Length; i++)
                {
                    if (speedRestrictions[i].speedLimit < minLimit)
                        minLimit = speedRestrictions[i].speedLimit;
                }
                if (minLimit < float.MaxValue)
                    speedLimit = minLimit;
            }

            segment.SetProperties(speedLimit, 0f, curveRadius);

            if (speedRestrictions != null)
            {
                for (int i = 0; i < speedRestrictions.Length; i++)
                {
                    var sr = speedRestrictions[i];
                    TrackSection section = new TrackSection
                    {
                        startDistance = sr.startStation - alignment.startStation,
                        endDistance = sr.endStation - alignment.startStation,
                        speedLimit = sr.speedLimit,
                        sectionType = SectionType.Mainline
                    };
                    section.startDistance = Mathf.Max(0f, section.startDistance);
                    section.endDistance = Mathf.Min(segment.Length, section.endDistance);
                    segment.AddSection(section);
                }
            }

            return segment;
        }

        public static float CalculateCurveRadiusFromPoints(Vector3 p1, Vector3 p2, Vector3 p3)
        {
            float a = Vector3.Distance(p2, p3);
            float b = Vector3.Distance(p1, p3);
            float c = Vector3.Distance(p1, p2);

            float s = (a + b + c) * 0.5f;
            float areaSq = s * (s - a) * (s - b) * (s - c);

            if (areaSq <= Mathf.Epsilon) return float.MaxValue;

            float area = Mathf.Sqrt(areaSq);
            float radius = (a * b * c) / (4f * area);

            return radius;
        }

        private static bool IsHeaderLine(string line)
        {
            string lower = line.ToLower();
            return lower.Contains("里程") || lower.Contains("mileage") ||
                   lower.Contains("东坐标") || lower.Contains("easting") ||
                   lower.Contains("北坐标") || lower.Contains("northing") ||
                   lower.Contains("高程") || lower.Contains("elevation");
        }
    }
}
