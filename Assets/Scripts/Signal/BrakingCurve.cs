using System;
using System.Collections.Generic;
using UnityEngine;

namespace CBTC.Sandtable.Signal
{
    public struct BrakingPoint
    {
        public float distance;
        public float speed;
        public bool isLimiting;
        public string source;

        public BrakingPoint(float distance, float speed, bool isLimiting = false, string source = "")
        {
            this.distance = distance;
            this.speed = speed;
            this.isLimiting = isLimiting;
            this.source = source;
        }
    }

    public static class BrakingCurve
    {
        private const float SampleInterval = 10f;

        public static List<BrakingPoint> CalculateServiceBrakingCurve(float currentSpeed, float currentDistance,
            float targetSpeed, float deceleration, float reactionTime = 2.0f)
        {
            var points = new List<BrakingPoint>();
            if (currentSpeed <= targetSpeed || deceleration <= 0f)
            {
                points.Add(new BrakingPoint(currentDistance, currentSpeed));
                return points;
            }

            float brakingDistance = (currentSpeed * currentSpeed - targetSpeed * targetSpeed) / (2f * deceleration);
            float reactionDistance = currentSpeed * reactionTime;
            float brakeStartDistance = currentDistance + reactionDistance;
            float brakeEndDistance = brakeStartDistance + brakingDistance;

            points.Add(new BrakingPoint(currentDistance, currentSpeed));

            if (reactionDistance > SampleInterval)
            {
                int reactionSamples = UnityEngine.Mathf.CeilToInt(reactionDistance / SampleInterval);
                for (int i = 1; i < reactionSamples; i++)
                {
                    float d = currentDistance + i * SampleInterval;
                    if (d >= brakeStartDistance) break;
                    points.Add(new BrakingPoint(d, currentSpeed));
                }
            }

            points.Add(new BrakingPoint(brakeStartDistance, currentSpeed, true, "ServiceBrakeStart"));

            if (brakingDistance > SampleInterval)
            {
                int brakingSamples = UnityEngine.Mathf.CeilToInt(brakingDistance / SampleInterval);
                for (int i = 1; i < brakingSamples; i++)
                {
                    float d = brakeStartDistance + i * SampleInterval;
                    if (d >= brakeEndDistance) break;
                    float distFromBrakeStart = d - brakeStartDistance;
                    float speedSquared = currentSpeed * currentSpeed - 2f * deceleration * distFromBrakeStart;
                    float speed = speedSquared > 0f ? (float)Math.Sqrt(speedSquared) : targetSpeed;
                    speed = Math.Max(speed, targetSpeed);
                    points.Add(new BrakingPoint(d, speed));
                }
            }

            points.Add(new BrakingPoint(brakeEndDistance, targetSpeed, true, "ServiceBrakeEnd"));

            return points;
        }

        public static List<BrakingPoint> CalculateEmergencyBrakingCurve(float currentSpeed, float currentDistance,
            float targetSpeed, float emergencyDeceleration)
        {
            var points = new List<BrakingPoint>();
            if (currentSpeed <= targetSpeed || emergencyDeceleration <= 0f)
            {
                points.Add(new BrakingPoint(currentDistance, currentSpeed));
                return points;
            }

            float brakingDistance = (currentSpeed * currentSpeed - targetSpeed * targetSpeed) / (2f * emergencyDeceleration);
            float brakeEndDistance = currentDistance + brakingDistance;

            points.Add(new BrakingPoint(currentDistance, currentSpeed, true, "EmergencyBrakeStart"));

            if (brakingDistance > SampleInterval)
            {
                int samples = UnityEngine.Mathf.CeilToInt(brakingDistance / SampleInterval);
                for (int i = 1; i < samples; i++)
                {
                    float d = currentDistance + i * SampleInterval;
                    if (d >= brakeEndDistance) break;
                    float distFromStart = d - currentDistance;
                    float speedSquared = currentSpeed * currentSpeed - 2f * emergencyDeceleration * distFromStart;
                    float speed = speedSquared > 0f ? (float)Math.Sqrt(speedSquared) : targetSpeed;
                    speed = Math.Max(speed, targetSpeed);
                    points.Add(new BrakingPoint(d, speed));
                }
            }

            points.Add(new BrakingPoint(brakeEndDistance, targetSpeed, true, "EmergencyBrakeEnd"));

            return points;
        }

        public static List<BrakingPoint> CalculateSpeedProfileWithRestrictions(float currentSpeed,
            float currentDistance, SpeedRestriction[] restrictions, float deceleration, float reactionTime)
        {
            var points = new List<BrakingPoint>();
            if (restrictions == null || restrictions.Length == 0)
            {
                points.Add(new BrakingPoint(currentDistance, currentSpeed));
                return points;
            }

            var sortedRestrictions = new List<SpeedRestriction>(restrictions);
            sortedRestrictions.Sort((a, b) => b.startDistance.CompareTo(a.startDistance));

            float effectiveSpeed = currentSpeed;
            float profileDistance = currentDistance;

            points.Add(new BrakingPoint(currentDistance, currentSpeed));

            for (int i = 0; i < sortedRestrictions.Count; i++)
            {
                var restriction = sortedRestrictions[i];
                float restrictionStart = restriction.startDistance;
                float restrictionSpeed = restriction.speedLimit;

                if (restrictionStart <= currentDistance) continue;
                if (restrictionSpeed >= effectiveSpeed) continue;

                float brakingDist = (effectiveSpeed * effectiveSpeed - restrictionSpeed * restrictionSpeed) / (2f * deceleration);
                float reactionDist = effectiveSpeed * reactionTime;
                float brakeTriggerDistance = restrictionStart - brakingDist - reactionDist;

                if (brakeTriggerDistance < currentDistance)
                {
                    float speedAtRestriction = (float)Math.Sqrt(
                        Math.Max(0f, effectiveSpeed * effectiveSpeed - 2f * deceleration * Math.Max(0f, restrictionStart - currentDistance - reactionDist)));
                    speedAtRestriction = Math.Max(speedAtRestriction, restrictionSpeed);
                    if (speedAtRestriction > restrictionSpeed)
                    {
                        effectiveSpeed = restrictionSpeed;
                    }
                }
                else
                {
                    if (brakeTriggerDistance > profileDistance)
                    {
                        AddConstantSpeedPoints(points, ref profileDistance, brakeTriggerDistance, effectiveSpeed, SampleInterval);
                    }

                    if (reactionDist > 0f && brakeTriggerDistance + reactionDist <= restrictionStart)
                    {
                        AddConstantSpeedPoints(points, ref profileDistance, brakeTriggerDistance + reactionDist, effectiveSpeed, SampleInterval);
                    }

                    AddBrakingDecelPoints(points, ref profileDistance, restrictionStart, effectiveSpeed, restrictionSpeed, deceleration, SampleInterval,
                        $"Restriction:{restriction.restrictionType}");

                    effectiveSpeed = restrictionSpeed;
                }
            }

            return points;
        }

        public static float CalculateSafeBrakingDistance(float currentSpeed, float targetSpeed,
            float deceleration, float reactionTime)
        {
            if (currentSpeed <= targetSpeed || deceleration <= 0f) return 0f;
            float brakingDist = (currentSpeed * currentSpeed - targetSpeed * targetSpeed) / (2f * deceleration);
            float reactionDist = currentSpeed * reactionTime;
            return brakingDist + reactionDist;
        }

        public static float CalculateBrakingDistanceToStop(float currentSpeed, float deceleration, float reactionTime)
        {
            return CalculateSafeBrakingDistance(currentSpeed, 0f, deceleration, reactionTime);
        }

        public static float GetTargetSpeedAtDistance(float distance, List<BrakingPoint> curve)
        {
            if (curve == null || curve.Count == 0) return 0f;

            if (distance <= curve[0].distance) return curve[0].speed;
            if (distance >= curve[curve.Count - 1].distance) return curve[curve.Count - 1].speed;

            for (int i = 0; i < curve.Count - 1; i++)
            {
                if (distance >= curve[i].distance && distance <= curve[i + 1].distance)
                {
                    float t = (distance - curve[i].distance) / (curve[i + 1].distance - curve[i].distance);
                    return curve[i].speed + t * (curve[i + 1].speed - curve[i].speed);
                }
            }

            return curve[curve.Count - 1].speed;
        }

        private static void AddConstantSpeedPoints(List<BrakingPoint> points, ref float profileDistance,
            float endDistance, float speed, float interval)
        {
            if (endDistance <= profileDistance) return;

            float totalDist = endDistance - profileDistance;
            int samples = UnityEngine.Mathf.CeilToInt(totalDist / interval);

            for (int i = 1; i < samples; i++)
            {
                float d = profileDistance + i * interval;
                if (d >= endDistance) break;
                points.Add(new BrakingPoint(d, speed));
            }

            points.Add(new BrakingPoint(endDistance, speed));
            profileDistance = endDistance;
        }

        private static void AddBrakingDecelPoints(List<BrakingPoint> points, ref float profileDistance,
            float endDistance, float startSpeed, float endSpeed, float deceleration, float interval, string source)
        {
            float brakingDist = endDistance - profileDistance;
            if (brakingDist <= 0f)
            {
                points.Add(new BrakingPoint(endDistance, endSpeed, true, source));
                profileDistance = endDistance;
                return;
            }

            int samples = UnityEngine.Mathf.CeilToInt(brakingDist / interval);
            for (int i = 1; i < samples; i++)
            {
                float d = profileDistance + i * interval;
                if (d >= endDistance) break;
                float distFromStart = d - profileDistance;
                float speedSquared = startSpeed * startSpeed - 2f * deceleration * distFromStart;
                float speed = speedSquared > 0f ? (float)Math.Sqrt(speedSquared) : endSpeed;
                speed = Math.Max(speed, endSpeed);
                points.Add(new BrakingPoint(d, speed));
            }

            points.Add(new BrakingPoint(endDistance, endSpeed, true, source));
            profileDistance = endDistance;
        }
    }
}
