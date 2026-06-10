using UnityEngine;

namespace CBTC.Sandtable.Train
{
    [CreateAssetMenu(fileName = "TrainPhysicsConfig", menuName = "CBTC/Train Physics Config")]
    public class TrainPhysicsConfig : ScriptableObject
    {
        public float mass = 40000f;
        public float length = 22f;
        public float width = 2.8f;
        public float height = 3.8f;

        public float maxSpeed = 22.22f;
        public float maxAcceleration = 1.0f;
        public float maxDeceleration = 1.2f;
        public float emergencyDeceleration = 1.5f;

        public float tractionEffort = 200000f;
        public float brakingEffort = 250000f;

        public float rollingResistanceCoeff = 0.002f;
        public float aerodynamicDragCoeff = 0.8f;

        public float wheelAdhesionCoeff = 0.2f;

        public float derailmentCurvatureThreshold = 0.01f;
        public float maxDistancePerPhysicsStep = 2f;

        private const float LateralCentripetalAccel = 0.65f;

        public float CalculateMaxSpeedAtCurvature(float curvatureRadius)
        {
            if (curvatureRadius <= 0f) return maxSpeed;
            float v = Mathf.Sqrt(LateralCentripetalAccel * curvatureRadius);
            return Mathf.Min(v, maxSpeed);
        }

        public float CalculateRollingResistance(float speed)
        {
            return rollingResistanceCoeff * mass * Physics.gravity.magnitude;
        }

        public float CalculateAerodynamicDrag(float speed)
        {
            return 0.5f * aerodynamicDragCoeff * 1.225f * width * height * speed * speed;
        }
    }
}
