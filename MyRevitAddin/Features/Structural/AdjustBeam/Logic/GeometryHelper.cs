using Autodesk.Revit.DB;
using MyRevitAddin.Core;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Logic
{
    public static class GeometryHelper
    {
        public static XYZ ComputeGapFromFaces(FamilyInstance beam, XYZ endpoint, XYZ outwardDir, Element targetElement, double clearance)
        {
            PlanarFace targetFace = SolidFaceUtils.FindFacingFace(targetElement, outwardDir);
            if (targetFace == null) return endpoint;

            XYZ targetNormal = targetFace.FaceNormal;
            XYZ targetOrigin = targetFace.Origin;

            PlanarFace beamEndFace = SolidFaceUtils.FindFacingFace(beam, outwardDir);

            double faceDist;
            if (beamEndFace != null)
            {
                faceDist = (beamEndFace.Origin - targetOrigin).DotProduct(targetNormal);
            }
            else
            {
                faceDist = (endpoint - targetOrigin).DotProduct(targetNormal);
            }

            double moveAmount = -clearance - faceDist;
            XYZ result = endpoint + targetNormal * moveAmount;

            return result;
        }

        public static XYZ ComputeInlineEndpoint(XYZ endpoint, XYZ outwardDir, FamilyInstance column, double halfGap,
            out bool needsVoidCut, out XYZ cutNormal, out XYZ cutOrigin)
        {
            needsVoidCut = false;
            cutNormal = XYZ.Zero;
            cutOrigin = XYZ.Zero;

            LocationPoint locPoint = column.Location as LocationPoint;
            XYZ colCenter = locPoint != null ? locPoint.Point : endpoint;

            Transform t = column.GetTransform();
            XYZ axisX = t.BasisX;
            XYZ axisY = t.BasisY;

            double dotX = Math.Abs(outwardDir.DotProduct(axisX));
            double dotY = Math.Abs(outwardDir.DotProduct(axisY));

            cutNormal = (dotX > dotY) ? axisX : axisY;

            if (cutNormal.DotProduct(outwardDir) < 0)
            {
                cutNormal = cutNormal.Negate();
            }

            cutOrigin = colCenter - cutNormal * halfGap;
            double dot = outwardDir.DotProduct(cutNormal);

            if (dot > 0.9998)
            {
                needsVoidCut = false;
                double t_intersect = (cutOrigin - endpoint).DotProduct(cutNormal) / dot;
                return endpoint + outwardDir * t_intersect;
            }
            else
            {
                needsVoidCut = true;
                return new XYZ(colCenter.X, colCenter.Y, endpoint.Z) + outwardDir * 1.0;
            }
        }

        /// <summary>
        /// Case 4: Non-collinear beam meets perpendicular beam - returns original endpoint.
        /// Opening Cut creation is handled separately in BeamEndCutter.CutBeamPerpendicular.
        /// </summary>
        public static XYZ ComputePerpBeamEndpoint(XYZ endpoint)
        {
            return endpoint;
        }

        /// <summary>
        /// Case 4b: Two collinear beams meet at a perpendicular beam.
        /// Shortens each beam by perpGap/2 and creates a void cut.
        /// </summary>
        public static XYZ ComputeInlinePerpEndpoint(XYZ endpoint, XYZ outwardDir, double halfPerpGap,
            out bool needsVoidCut, out XYZ cutNormal, out XYZ cutOrigin)
        {
            needsVoidCut = true;

            // cutNormal = outward direction (direction extending away from beam at this endpoint)
            cutNormal = outwardDir;

            // Shorten endpoint: step back by halfPerpGap
            XYZ newEndpoint = endpoint - outwardDir * halfPerpGap;

            // cutOrigin = cut plane position = newEndpoint
            cutOrigin = newEndpoint;

            return newEndpoint;
        }
    }
}
