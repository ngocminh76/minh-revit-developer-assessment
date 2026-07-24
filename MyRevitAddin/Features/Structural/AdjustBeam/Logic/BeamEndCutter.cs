using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Logic
{
    /// <summary>
    /// Tạo Opening Cut (void) tại đầu dầm để mặt cắt song song với cạnh cột.
    /// Chỉ cần dùng khi 2 dầm inline có hướng khác nhau tại cùng 1 cột.
    /// </summary>
    public class BeamEndCutter
    {
        private const double AngleThreshold = 0.9998; // ~1° → bỏ qua nếu gần trùng

        private Family _voidFamily;
        private FamilySymbol _voidSymbol;

        /// <summary>
        /// <param name="doc">Document</param>
        /// <param name="beam">Dầm cần cắt</param>
        /// <param name="cutOrigin">Vị trí điểm cắt (đã được dời halfGap so với tâm cột)</param>
        /// <param name="beamOutwardDir">Hướng ra ngoài đầu dầm</param>
        /// <param name="cutNormal">Pháp tuyến mặt cắt (hướng ra ngoài dầm, về phía cột)</param>
        public void CutBeamEnd(Document doc, FamilyInstance beam, XYZ cutOrigin, XYZ beamOutwardDir, XYZ cutNormal)
        {
            string logPath = @"D:\03.MINH\REVIT\RevitTest\beam_adjust_log.txt";

            // 1. Tạo/lấy void symbol
            FamilySymbol symbol = GetOrCreateVoidSymbol(doc);
            if (symbol == null)
            {
                Log(logPath, "[ERROR] Void symbol = NULL");
                return;
            }

            // 2. Đặt void tại cutOrigin
            FamilyInstance voidInst = null;
            try
            {
                voidInst = doc.Create.NewFamilyInstance(
                    cutOrigin, symbol, StructuralType.NonStructural);
                Log(logPath, $"[OK] Void created: Id={voidInst.Id}");
            }
            catch (Exception ex)
            {
                Log(logPath, $"[ERROR] NewFamilyInstance: {ex.Message}");
                return;
            }

            // 3. Xoay void: +X → cutNormal (ra ngoài, cắt phần thừa của dầm)
            double angle = Math.Atan2(cutNormal.Y, cutNormal.X);
            try
            {
                if (Math.Abs(angle) > 1e-10)
                {
                    Line zAxis = Line.CreateBound(cutOrigin, cutOrigin + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, voidInst.Id, zAxis, angle);
                }
                Log(logPath, $"[OK] Rotated {angle * 180 / Math.PI:F2}°");
            }
            catch (Exception ex)
            {
                Log(logPath, $"[ERROR] Rotate: {ex.Message}");
            }

            // 4. Áp dụng cut
            bool canCut = InstanceVoidCutUtils.CanBeCutWithVoid(beam);
            Log(logPath, $"[CHECK] CanBeCutWithVoid(beam {beam.Id}) = {canCut}");

            try
            {
                if (canCut)
                {
                    InstanceVoidCutUtils.AddInstanceVoidCut(doc, beam, voidInst);
                    Log(logPath, $"[OK] InstanceVoidCut: beam={beam.Id}, void={voidInst.Id}");
                }
                else
                {
                    SolidSolidCutUtils.AddCutBetweenSolids(doc, beam, voidInst);
                    Log(logPath, $"[OK] SolidSolidCut: beam={beam.Id}, void={voidInst.Id}");
                }
            }
            catch (Exception ex)
            {
                Log(logPath, $"[ERROR] Cut failed: {ex.Message}");
            }

            // 5. Summary
            Log(logPath, $"\n--- OPENING CUT: Dầm [{beam.Id}] ---\n"
                + $"  cutOrigin:      ({cutOrigin.X:F6}, {cutOrigin.Y:F6}, {cutOrigin.Z:F6})\n"
                + $"  beamOutwardDir: ({beamOutwardDir.X:F6}, {beamOutwardDir.Y:F6})\n"
                + $"  cutNormal:      ({cutNormal.X:F6}, {cutNormal.Y:F6})\n"
                + $"  voidId:         {voidInst.Id}\n");
        }

        private void Log(string path, string msg)
        {
            try { File.AppendAllText(path, msg + "\n"); } catch { }
        }

        #region Private: Void Family

        private FamilySymbol GetOrCreateVoidSymbol(Document doc)
        {
            if (_voidSymbol != null) return _voidSymbol;

            // Tìm family đã load
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
        /// Tạo family GenericModel chứa void extrusion.
        /// Void: hộp lớn (3m x 3m x 1.5m), extrusion dọc trục +X.
        /// Origin tại mặt cắt (X=0). Void nằm phía X > 0.
        /// </summary>
        private Family CreateVoidFamily(Document doc)
        {
            var app = doc.Application;

            // Tìm template Generic Model
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

                    double size = 5.0;  // 5 ft ≈ 1524mm (nửa cạnh)
                    double depth = 3.0; // 3 ft ≈ 914mm (độ sâu cắt)

                    // Profile trên mặt phẳng YZ (vuông góc trục X)
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

                    // Sketch plane vuông góc trục X
                    SketchPlane skPlane = SketchPlane.Create(famDoc,
                        Plane.CreateByNormalAndOrigin(XYZ.BasisX, XYZ.Zero));

                    // Void extrusion dọc +X
                    Extrusion ext = famDoc.FamilyCreate.NewExtrusion(
                        false,       // isSolid = false → VOID
                        profileArray,
                        skPlane,
                        depth);

                    // Bật "Cut with Voids When Loaded"
                    famDoc.OwnerFamily
                        .get_Parameter(BuiltInParameter.FAMILY_ALLOW_CUT_WITH_VOIDS)
                        .Set(1);

                    tx.Commit();
                }

                // Lưu tạm + load vào project
                string tempPath = Path.Combine(Path.GetTempPath(), "BeamEndCutVoid.rfa");
                famDoc.SaveAs(tempPath, new SaveAsOptions { OverwriteExistingFile = true });
                famDoc.Close(false);
                famDoc = null;

                doc.LoadFamily(tempPath, out family);

                try { File.Delete(tempPath); } catch { }
            }
            catch (Exception ex)
            {
                try
                {
                    string logPath = @"D:\03.MINH\REVIT\RevitTest\beam_adjust_log.txt";
                    File.AppendAllText(logPath, $"\n[ERROR] CreateVoidFamily: {ex.Message}\n");
                }
                catch { }
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

            // Thử tìm trong thư mục con
            foreach (string c in candidates)
            {
                if (File.Exists(c)) return c;
            }

            // Fallback: tìm bất kỳ file .rft nào có "Generic" trong tên
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

        #region Private: Top Face

        private PlanarFace FindTopFace(Element element)
        {
            PlanarFace bestFace = null;
            double bestArea = 0;

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
                                PlanarFace pf = GetTopFaceInSolid(s, ref bestArea);
                                if (pf != null) bestFace = pf;
                            }
                        }
                    }
                }
                else if (solid.Volume > 0)
                {
                    PlanarFace pf = GetTopFaceInSolid(solid, ref bestArea);
                    if (pf != null) bestFace = pf;
                }
            }

            return bestFace;
        }

        private PlanarFace GetTopFaceInSolid(Solid solid, ref double bestArea)
        {
            PlanarFace bestFace = null;
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace pf)
                {
                    if (pf.FaceNormal.Z < 0.9) continue;
                    if (pf.Area > bestArea)
                    {
                        bestArea = pf.Area;
                        bestFace = pf;
                    }
                }
            }
            return bestFace;
        }

        #endregion
    }
}
