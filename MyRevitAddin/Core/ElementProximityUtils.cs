using Autodesk.Revit.DB;

namespace MyRevitAddin.Core
{
    public static class ElementProximityUtils
    {
        private const double NearbyTolerance = 3.0;

        public static FamilyInstance FindNearestColumn(XYZ point, IEnumerable<FamilyInstance> columns)
        {
            FamilyInstance nearest = null;
            double minDist = NearbyTolerance;
            foreach (var col in columns)
            {
                var lp = col.Location as LocationPoint;
                if (lp == null) continue;
                double dist = MathUtils.Dist2D(point, lp.Point);
                if (dist < minDist) { minDist = dist; nearest = col; }
            }
            return nearest;
        }

        public static Wall FindNearestWall(XYZ point, IEnumerable<Wall> walls)
        {
            Wall nearest = null;
            double minDist = NearbyTolerance;
            foreach (var wall in walls)
            {
                var wlc = wall.Location as LocationCurve;
                if (wlc == null) continue;
                var wl = wlc.Curve as Line;
                if (wl == null) continue;
                double dist = MathUtils.DistToSegment2D(point, wl.GetEndPoint(0), wl.GetEndPoint(1));
                if (dist < minDist) { minDist = dist; nearest = wall; }
            }
            return nearest;
        }

        public static FamilyInstance FindInlineBeamAtColumn(FamilyInstance thisBeam, FamilyInstance column, XYZ outwardDir, IEnumerable<FamilyInstance> beams)
        {
            XYZ colCenter = ((LocationPoint)column.Location).Point;
            foreach (var other in beams)
            {
                if (other.Id == thisBeam.Id) continue;
                var olc = other.Location as LocationCurve;
                if (olc == null) continue;
                var ol = olc.Curve as Line;
                if (ol == null) continue;

                if (Math.Abs(outwardDir.DotProduct(ol.Direction)) < 0.966) continue;

                if (MathUtils.Dist2D(ol.GetEndPoint(0), colCenter) < NearbyTolerance ||
                    MathUtils.Dist2D(ol.GetEndPoint(1), colCenter) < NearbyTolerance)
                    return other;
            }
            return null;
        }

        public static FamilyInstance FindPerpendicularBeam(FamilyInstance thisBeam, XYZ endpoint, XYZ beamDir, IEnumerable<FamilyInstance> beams)
        {
            FamilyInstance nearest = null;
            double minDist = NearbyTolerance;
            foreach (var other in beams)
            {
                if (other.Id == thisBeam.Id) continue;
                var olc = other.Location as LocationCurve;
                if (olc == null) continue;
                var ol = olc.Curve as Line;
                if (ol == null) continue;

                if (Math.Abs(beamDir.DotProduct(ol.Direction)) > 0.259) continue;
                XYZ closest = MathUtils.ClosestOnSegment(endpoint, ol.GetEndPoint(0), ol.GetEndPoint(1));
                double dist = endpoint.DistanceTo(closest);
                if (dist < minDist) { minDist = dist; nearest = other; }
            }
            return nearest;
        }
    }
}
