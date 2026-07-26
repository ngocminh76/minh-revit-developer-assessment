using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.IO;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Logic
{
    /// <summary>
    /// Creates an opening cut (void) at beam ends to make the cut face parallel to column edges.
    /// Used when collinear beams have different orientations at the same column.
    /// </summary>
    public class BeamEndCutter
    {

        private Family _voidFamily;
        private FamilySymbol _voidSymbol;

        /// <summary>
        /// Cuts the end of a beam using a void family.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="beam">The beam instance to cut.</param>
        /// <param name="cutOrigin">The cut origin position (shifted by halfGap from column center).</param>
        /// <param name="beamOutwardDir">Outward direction of the beam end.</param>
        /// <param name="cutNormal">Normal of the cut plane (pointing outward from beam toward column).</param>
        public void CutBeamEnd(Document doc, FamilyInstance beam, XYZ cutOrigin, XYZ beamOutwardDir, XYZ cutNormal)
        {
            // 1. Get or create void symbol
            FamilySymbol symbol = GetOrCreateVoidSymbol(doc);
            if (symbol == null) return;

            // 2. Place void instance at cutOrigin
            FamilyInstance voidInst = null;
            try
            {
                voidInst = doc.Create.NewFamilyInstance(
                    cutOrigin, symbol, StructuralType.NonStructural);
            }
            catch (Exception)
            {
                return;
            }

            // 3. Rotate void: +X -> cutNormal (outward, cutting beam excess)
            double angle = Math.Atan2(cutNormal.Y, cutNormal.X);
            try
            {
                if (Math.Abs(angle) > 1e-10)
                {
                    Line zAxis = Line.CreateBound(cutOrigin, cutOrigin + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, voidInst.Id, zAxis, angle);
                }
            }
            catch (Exception)
            {
            }

            // 4. Apply cut
            bool canCut = InstanceVoidCutUtils.CanBeCutWithVoid(beam);

            try
            {
                if (canCut)
                {
                    InstanceVoidCutUtils.AddInstanceVoidCut(doc, beam, voidInst);
                }
                else
                {
                    SolidSolidCutUtils.AddCutBetweenSolids(doc, beam, voidInst);
                }
            }
            catch (Exception)
            {
            }
        }

        #region Private: Void Family

        private FamilySymbol GetOrCreateVoidSymbol(Document doc)
        {
            if (_voidSymbol != null) return _voidSymbol;

            // Find already loaded family
            _voidFamily = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.Name == "BeamEndCutVoid");

            if (_voidFamily == null)
                _voidFamily = CreateVoidFamily(doc);

            if (_voidFamily != null)
            {
                var symId = _voidFamily.GetFamilySymbolIds().FirstOrDefault();
                if (symId != null && symId != ElementId.InvalidElementId)
                {
                    _voidSymbol = doc.GetElement(symId) as FamilySymbol;
                    if (_voidSymbol != null && !_voidSymbol.IsActive)
                        _voidSymbol.Activate();
                }
            }

            return _voidSymbol;
        }

        /// <summary>
        /// Creates a Generic Model family containing a void extrusion.
        /// Void: large box (3m x 3m x 1.5m), extrusion along +X axis.
        /// Origin at the cut plane (X=0). Void is located at X > 0.
        /// </summary>
        private Family CreateVoidFamily(Document doc)
        {
            var app = doc.Application;

            // Find Generic Model template
            string templatePath = FindFamilyTemplate(app);
            if (templatePath == null) return null;

            Document famDoc = null;
            Family family = null;

            try
            {
                famDoc = app.NewFamilyDocument(templatePath);

                using (Transaction tx = new Transaction(famDoc, "Create Void"))
                {
                    tx.Start();

                    double size = 15.0;  // 15 ft ~ 4572mm (half side)
                    double depth = 10.0; // 10 ft ~ 3048mm (cut depth)

                    // Profile on YZ plane (perpendicular to X-axis)
                    XYZ p1 = new XYZ(0, -size, -size);
                    XYZ p2 = new XYZ(0, size, -size);
                    XYZ p3 = new XYZ(0, size, size);
                    XYZ p4 = new XYZ(0, -size, size);

                    CurveArray profile = new CurveArray();
                    profile.Append(Line.CreateBound(p1, p2));
                    profile.Append(Line.CreateBound(p2, p3));
                    profile.Append(Line.CreateBound(p3, p4));
                    profile.Append(Line.CreateBound(p4, p1));

                    CurveArrArray profileArray = new CurveArrArray();
                    profileArray.Append(profile);

                    // Sketch plane perpendicular to X-axis
                    SketchPlane skPlane = SketchPlane.Create(famDoc,
                        Plane.CreateByNormalAndOrigin(XYZ.BasisX, XYZ.Zero));

                    // Void extrusion along +X
                    Extrusion ext = famDoc.FamilyCreate.NewExtrusion(
                        false,       // isSolid = false -> VOID
                        profileArray,
                        skPlane,
                        depth);

                    // Enable "Cut with Voids When Loaded"
                    famDoc.OwnerFamily
                        .get_Parameter(BuiltInParameter.FAMILY_ALLOW_CUT_WITH_VOIDS)
                        .Set(1);

                    tx.Commit();
                }

                // Save temporarily and load into project
                string tempPath = Path.Combine(Path.GetTempPath(), "BeamEndCutVoid.rfa");
                famDoc.SaveAs(tempPath, new SaveAsOptions { OverwriteExistingFile = true });
                famDoc.Close(false);
                famDoc = null;

                doc.LoadFamily(tempPath, out family);

                try { File.Delete(tempPath); } catch { }
            }
            catch (Exception)
            {
            }
            finally
            {
                if (famDoc != null && famDoc.IsValidObject)
                    famDoc.Close(false);
            }

            return family;
        }

        private string FindFamilyTemplate(Autodesk.Revit.ApplicationServices.Application app)
        {
            string basePath = app.FamilyTemplatePath;

            string[] candidates = new[]
            {
                Path.Combine(basePath, "Metric Generic Model.rft"),
                Path.Combine(basePath, "Generic Model.rft"),
                Path.Combine(basePath, "English", "Metric Generic Model.rft"),
            };

            // Search subdirectories
            foreach (string c in candidates)
            {
                if (File.Exists(c)) return c;
            }

            // Fallback: find any .rft file containing "Generic" in its name
            if (Directory.Exists(basePath))
            {
                foreach (string f in Directory.GetFiles(basePath, "*.rft", SearchOption.AllDirectories))
                {
                    if (f.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) >= 0)
                        return f;
                }
            }

            return null;
        }
        #endregion

        #region Perpendicular Beam Cut (slab void)

        /// <summary>
        /// Creates a slab void cut on a beam at the intersection zone with a perpendicular beam.
        /// The void cuts only within the intersection region, creating clearance on both sides.
        /// </summary>
        public void CutBeamPerpendicular(Document doc, FamilyInstance beam, Element perpBeam, XYZ outwardDir, double clearance)
        {
            // 1. Find the near and far web faces of the perpendicular beam
            PlanarFace nearWeb = Core.SolidFaceUtils.FindNearWebFace(perpBeam, outwardDir);
            PlanarFace farWeb = Core.SolidFaceUtils.FindNearWebFace(perpBeam, outwardDir.Negate());
            if (nearWeb == null || farWeb == null) return;

            // 2. Calculate distance between web faces along outwardDir
            //    nearWeb.FaceNormal points TOWARD beam (opposite to outwardDir)
            //    farWeb.FaceNormal points in same direction as outwardDir
            //    webWidth = distance between planes
            double nearPlane = nearWeb.Origin.DotProduct(outwardDir);
            double farPlane = farWeb.Origin.DotProduct(outwardDir);
            double webWidth = Math.Abs(farPlane - nearPlane);

            // 3. Slab depth = web width + 2 * clearance
            double slabDepth = webWidth + 2.0 * clearance;

            // 4. Find center of perpendicular beam on current beam axis
            double centerPlane = (nearPlane + farPlane) / 2.0;
            LocationCurve beamLoc = beam.Location as LocationCurve;
            if (beamLoc == null) return;
            Line beamLine = beamLoc.Curve as Line;
            if (beamLine == null) return;

            // Project center onto beam axis
            XYZ beamStart = beamLine.GetEndPoint(0);
            XYZ beamDir = beamLine.Direction;
            // Find point on beam axis closest to perpendicular beam center
            LocationCurve perpLoc = perpBeam.Location as LocationCurve;
            XYZ perpCenter = XYZ.Zero;
            if (perpLoc != null)
            {
                Line perpLine = perpLoc.Curve as Line;
                if (perpLine != null)
                {
                    perpCenter = Core.MathUtils.ClosestOnSegment(
                        beamLine.Evaluate(0.5, true), // midpoint of beam
                        perpLine.GetEndPoint(0), perpLine.GetEndPoint(1));
                }
            }

            // Void placement position: cut center on beam axis projected from perpBeam center
            XYZ slabCenter = Core.MathUtils.ProjectPointOnLine(perpCenter, beamStart, beamDir);

            // 5. Create slab void family
            FamilySymbol slabSymbol = GetOrCreateSlabVoidSymbol(doc, slabDepth);
            if (slabSymbol == null) return;

            // 6. Place void instance at slabCenter
            FamilyInstance voidInst = null;
            try
            {
                voidInst = doc.Create.NewFamilyInstance(
                    slabCenter, slabSymbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
            }
            catch { return; }

            // 7. Rotate void: +X -> outwardDir
            try
            {
                double angle = Math.Atan2(outwardDir.Y, outwardDir.X);
                if (Math.Abs(angle) > 1e-10)
                {
                    Line zAxis = Line.CreateBound(slabCenter, slabCenter + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, voidInst.Id, zAxis, angle);
                }
            }
            catch { }

            // 8. Apply cut
            try
            {
                if (InstanceVoidCutUtils.CanBeCutWithVoid(beam))
                    InstanceVoidCutUtils.AddInstanceVoidCut(doc, beam, voidInst);
                else
                    SolidSolidCutUtils.AddCutBetweenSolids(doc, beam, voidInst);
            }
            catch { }
        }

        private FamilySymbol _slabVoidSymbol;
        private double _slabVoidDepth;

        private FamilySymbol GetOrCreateSlabVoidSymbol(Document doc, double depth)
        {
            // Reuse if created with matching depth (tolerance < 0.01ft ~ 3mm)
            if (_slabVoidSymbol != null && Math.Abs(_slabVoidDepth - depth) < 0.01)
                return _slabVoidSymbol;

            Family family = CreateSlabVoidFamily(doc, depth);
            if (family == null) return null;

            var symId = family.GetFamilySymbolIds().FirstOrDefault();
            if (symId == null || symId == ElementId.InvalidElementId) return null;

            _slabVoidSymbol = doc.GetElement(symId) as FamilySymbol;
            if (_slabVoidSymbol != null && !_slabVoidSymbol.IsActive)
                _slabVoidSymbol.Activate();

            _slabVoidDepth = depth;
            return _slabVoidSymbol;
        }

        /// <summary>
        /// Creates a slab-like void family: large box (15ft x 15ft) with thickness equal to slabDepth.
        /// Void is symmetric about the origin: from X=-depth/2 to X=+depth/2.
        /// </summary>
        private Family CreateSlabVoidFamily(Document doc, double slabDepth)
        {
            var app = doc.Application;
            string templatePath = FindFamilyTemplate(app);
            if (templatePath == null) return null;

            Document famDoc = null;
            Family family = null;
            string familyName = "BeamPerpCutVoidSlab";

            try
            {
                // Delete existing family if present
                Family existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.Name == familyName);
                if (existing != null)
                    doc.Delete(existing.Id);

                famDoc = app.NewFamilyDocument(templatePath);

                using (Transaction tx = new Transaction(famDoc, "Create Slab Void"))
                {
                    tx.Start();

                    double size = 15.0; // 15 ft ~ 4572mm (half side)
                    double halfDepth = slabDepth / 2.0;

                    // Profile on YZ plane at X = -halfDepth
                    XYZ p1 = new XYZ(-halfDepth, -size, -size);
                    XYZ p2 = new XYZ(-halfDepth, size, -size);
                    XYZ p3 = new XYZ(-halfDepth, size, size);
                    XYZ p4 = new XYZ(-halfDepth, -size, size);

                    CurveArray profile = new CurveArray();
                    profile.Append(Line.CreateBound(p1, p2));
                    profile.Append(Line.CreateBound(p2, p3));
                    profile.Append(Line.CreateBound(p3, p4));
                    profile.Append(Line.CreateBound(p4, p1));

                    CurveArrArray profileArray = new CurveArrArray();
                    profileArray.Append(profile);

                    SketchPlane skPlane = SketchPlane.Create(famDoc,
                        Plane.CreateByNormalAndOrigin(XYZ.BasisX, new XYZ(-halfDepth, 0, 0)));

                    // Extrusion along +X, from -halfDepth to +halfDepth
                    Extrusion ext = famDoc.FamilyCreate.NewExtrusion(
                        false,       // isSolid = false -> VOID
                        profileArray,
                        skPlane,
                        slabDepth);  // depth = slabDepth

                    // Enable "Cut with Voids When Loaded"
                    famDoc.OwnerFamily
                        .get_Parameter(BuiltInParameter.FAMILY_ALLOW_CUT_WITH_VOIDS)
                        .Set(1);

                    tx.Commit();
                }

                string tempPath = Path.Combine(Path.GetTempPath(), familyName + ".rfa");
                famDoc.SaveAs(tempPath, new SaveAsOptions { OverwriteExistingFile = true });
                famDoc.Close(false);
                famDoc = null;

                doc.LoadFamily(tempPath, new FamilyLoadOverwriteOption(), out family);

                try { File.Delete(tempPath); } catch { }
            }
            catch { }
            finally
            {
                if (famDoc != null && famDoc.IsValidObject)
                    famDoc.Close(false);
            }

            return family;
        }

        private class FamilyLoadOverwriteOption : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }

        #endregion
    }
}
