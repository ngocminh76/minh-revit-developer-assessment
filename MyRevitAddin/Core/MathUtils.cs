using Autodesk.Revit.DB;

namespace MyRevitAddin.Core
{
    public static class MathUtils
    {
        public static XYZ ProjectPointOnLine(XYZ point, XYZ lineOrigin, XYZ lineDir)
        {
            XYZ d = lineDir.Normalize();
            return lineOrigin + d * (point - lineOrigin).DotProduct(d);
        }

        public static XYZ ClosestOnSegment(XYZ p, XYZ a, XYZ b)
        {
            XYZ ab = b - a;
            double len = ab.GetLength();
            if (len < 1e-10) return a;
            XYZ dir = ab / len;
            double t = Math.Max(0, Math.Min(len, (p - a).DotProduct(dir)));
            return a + dir * t;
        }

        public static double Dist2D(XYZ a, XYZ b)
        {
            return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        }

        public static double DistToSegment2D(XYZ p, XYZ a, XYZ b)
        {
            XYZ p2 = new XYZ(p.X, p.Y, 0);
            XYZ a2 = new XYZ(a.X, a.Y, 0);
            XYZ b2 = new XYZ(b.X, b.Y, 0);
            return p2.DistanceTo(ClosestOnSegment(p2, a2, b2));
        }
    }
}
