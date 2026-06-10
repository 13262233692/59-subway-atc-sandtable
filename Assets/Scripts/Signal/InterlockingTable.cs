using System.Collections.Generic;

namespace CBTC.Sandtable.Signal
{
    public struct RouteEntry
    {
        public string routeId;
        public string startSignal;
        public string endSignal;
        public string[] requiredTrackCircuits;
        public string[] conflictingRoutes;

        public RouteEntry(string routeId, string startSignal, string endSignal,
            string[] requiredTrackCircuits, string[] conflictingRoutes)
        {
            this.routeId = routeId;
            this.startSignal = startSignal;
            this.endSignal = endSignal;
            this.requiredTrackCircuits = requiredTrackCircuits;
            this.conflictingRoutes = conflictingRoutes;
        }
    }

    public class InterlockingTable
    {
        private List<RouteEntry> _routes = new List<RouteEntry>();
        private HashSet<string> _establishedRoutes = new HashSet<string>();
        private Dictionary<string, RouteEntry> _routeIndex = new Dictionary<string, RouteEntry>();

        public List<RouteEntry> Routes => _routes;
        public HashSet<string> EstablishedRoutes => _establishedRoutes;

        public void AddRoute(RouteEntry entry)
        {
            _routes.Add(entry);
            _routeIndex[entry.routeId] = entry;
        }

        public void RemoveRoute(string routeId)
        {
            for (int i = _routes.Count - 1; i >= 0; i--)
            {
                if (_routes[i].routeId == routeId)
                {
                    _routes.RemoveAt(i);
                    break;
                }
            }
            _routeIndex.Remove(routeId);
            _establishedRoutes.Remove(routeId);
        }

        public bool RequestRoute(string routeId)
        {
            if (!_routeIndex.TryGetValue(routeId, out RouteEntry entry)) return false;

            if (_establishedRoutes.Contains(routeId)) return false;

            string[] conflicting = entry.conflictingRoutes;
            for (int i = 0; i < conflicting.Length; i++)
            {
                if (_establishedRoutes.Contains(conflicting[i]))
                {
                    return false;
                }
            }

            _establishedRoutes.Add(routeId);
            return true;
        }

        public void ReleaseRoute(string routeId)
        {
            _establishedRoutes.Remove(routeId);
        }

        public bool IsRouteAvailable(string routeId)
        {
            if (!_routeIndex.TryGetValue(routeId, out RouteEntry entry)) return false;

            if (_establishedRoutes.Contains(routeId)) return false;

            string[] conflicting = entry.conflictingRoutes;
            for (int i = 0; i < conflicting.Length; i++)
            {
                if (_establishedRoutes.Contains(conflicting[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public List<string> GetConflictingRoutes(string routeId)
        {
            List<string> result = new List<string>();

            if (!_routeIndex.TryGetValue(routeId, out RouteEntry entry)) return result;

            string[] conflicting = entry.conflictingRoutes;
            for (int i = 0; i < conflicting.Length; i++)
            {
                result.Add(conflicting[i]);
            }

            return result;
        }
    }
}
