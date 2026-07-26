using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using MyRevitAddin.Core;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Logic
{
    public static class GeometryHelper
    {
        private const double MmToFeet = 1.0 / 304.8;
        
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

        public static XYZ ComputePerpBeamEndpoint(XYZ endpoint, XYZ outwardDir, FamilyInstance perpBeam, double clearance)
        {
            XYZ inwardDir = outwardDir.Negate();

            LocationCurve perpLoc = perpBeam.Location as LocationCurve;
            if (perpLoc == null) return endpoint;
            Line perpLine = perpLoc.Curve as Line;
            if (perpLine == null) return endpoint;

            XYZ closestOnPerp = MathUtils.ClosestOnSegment(endpoint, perpLine.GetEndPoint(0), perpLine.GetEndPoint(1));
            XYZ centerOnOurLine = MathUtils.ProjectPointOnLine(closestOnPerp, endpoint, outwardDir);

            BoundingBoxXYZ bb = perpBeam.get_BoundingBox(null);
            if (bb == null) return endpoint;

            double dimX = bb.Max.X - bb.Min.X;
            double dimY = bb.Max.Y - bb.Min.Y;

            double crossSectionWidth = Math.Min(dimX, dimY);
            double halfCrossWidth = crossSectionWidth / 2.0;

            return centerOnOurLine + inwardDir * (halfCrossWidth + clearance);
        }
    }
}
