using System.Collections.Generic;

namespace CBTC.Sandtable.Signal
{
    public enum RestrictionType
    {
        Permanent,
        Temporary,
        Weather,
        Curve,
        StationApproach
    }

    public struct SpeedRestriction
    {
        public float startDistance;
        public float endDistance;
        public float speedLimit;
        public RestrictionType restrictionType;

        public SpeedRestriction(float startDistance, float endDistance, float speedLimit, RestrictionType restrictionType)
        {
            this.startDistance = startDistance;
            this.endDistance = endDistance;
            this.speedLimit = speedLimit;
            this.restrictionType = restrictionType;
        }
    }

    public class SpeedRestrictionProfile
    {
        private List<SpeedRestriction> _restrictions = new List<SpeedRestriction>();

        public List<SpeedRestriction> Restrictions => _restrictions;

        public SpeedRestriction? GetActiveRestrictionAt(float distance)
        {
            SpeedRestriction? active = null;
            float lowestSpeed = float.MaxValue;

            for (int i = 0; i < _restrictions.Count; i++)
            {
                var r = _restrictions[i];
                if (distance >= r.startDistance && distance <= r.endDistance)
                {
                    if (r.speedLimit < lowestSpeed)
                    {
                        lowestSpeed = r.speedLimit;
                        active = r;
                    }
                }
            }

            return active;
        }

        public List<SpeedRestriction> GetUpcomingRestrictions(float distance, float lookAheadDistance)
        {
            var upcoming = new List<SpeedRestriction>();
            float lookAheadEnd = distance + lookAheadDistance;

            for (int i = 0; i < _restrictions.Count; i++)
            {
                var r = _restrictions[i];
                if (r.startDistance >= distance && r.startDistance <= lookAheadEnd)
                {
                    upcoming.Add(r);
                }
                else if (r.startDistance < distance && r.endDistance > distance && r.endDistance <= lookAheadEnd)
                {
                    upcoming.Add(r);
                }
            }

            upcoming.Sort((a, b) => a.startDistance.CompareTo(b.startDistance));
            return upcoming;
        }

        public void AddRestriction(SpeedRestriction restriction)
        {
            _restrictions.Add(restriction);
        }

        public void RemoveRestriction(SpeedRestriction restriction)
        {
            for (int i = _restrictions.Count - 1; i >= 0; i--)
            {
                var r = _restrictions[i];
                if (r.startDistance == restriction.startDistance &&
                    r.endDistance == restriction.endDistance &&
                    r.speedLimit == restriction.speedLimit &&
                    r.restrictionType == restriction.restrictionType)
                {
                    _restrictions.RemoveAt(i);
                    return;
                }
            }
        }

        public float GetEffectiveSpeedLimit(float distance)
        {
            float effective = float.MaxValue;
            bool found = false;

            for (int i = 0; i < _restrictions.Count; i++)
            {
                var r = _restrictions[i];
                if (distance >= r.startDistance && distance <= r.endDistance)
                {
                    if (r.speedLimit < effective)
                    {
                        effective = r.speedLimit;
                        found = true;
                    }
                }
            }

            return found ? effective : float.MaxValue;
        }
    }
}
