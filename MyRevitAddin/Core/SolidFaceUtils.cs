using Autodesk.Revit.DB;

namespace MyRevitAddin.Core
{
    public static class SolidFaceUtils
    {
        public static PlanarFace FindFacingFace(Element element, XYZ outwardDir)
        {
            PlanarFace bestFace = null;
            double bestDot = -1;
            double bestProjection = double.MinValue;

            GeometryElement geomElem = element.get_Geometry(new Options { ComputeReferences = true });
            if (geomElem == null) return null;

            foreach (GeometryObject geomObj in geomElem)
            {
                Solid solid = geomObj as Solid;
                if (solid == null)
                {
                    if (geomObj is GeometryInstance geomInst)
                    {
                        GeometryElement instGeom = geomInst.GetInstanceGeometry();
                        foreach (GeometryObject instObj in instGeom)
                        {
                            if (instObj is Solid s && s.Volume > 0)
                            {
                                PlanarFace pf = GetBestFacingFaceInSolid(s, outwardDir, ref bestDot, ref bestProjection);
                                if (pf != null) bestFace = pf;
                            }
                        }
                    }
                }
                else if (solid.Volume > 0)
                {
                    PlanarFace pf = GetBestFacingFaceInSolid(solid, outwardDir, ref bestDot, ref bestProjection);
                    if (pf != null) bestFace = pf;
                }
            }

            return bestFace;
        }

        private static PlanarFace GetBestFacingFaceInSolid(Solid solid, XYZ outwardDir, ref double bestDot, ref double bestProjection)
        {
            PlanarFace bestFace = null;
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace pf)
                {
                    if (Math.Abs(pf.FaceNormal.Z) > 0.3) continue;
                    if (pf.Area < 0.1) continue;

                    double dot = outwardDir.DotProduct(pf.FaceNormal);
                    double projection = pf.Origin.DotProduct(pf.FaceNormal);

                    if (dot > bestDot + 0.01)
                    {
                        bestDot = dot;
                        bestProjection = projection;
                        bestFace = pf;
                    }
                    else if (Math.Abs(dot - bestDot) <= 0.01)
                    {
                        if (projection > bestProjection)
                        {
                            bestDot = dot;
                            bestProjection = projection;
                            bestFace = pf;
                        }
                    }
                }
            }
            return bestFace;
        }
    }
}
