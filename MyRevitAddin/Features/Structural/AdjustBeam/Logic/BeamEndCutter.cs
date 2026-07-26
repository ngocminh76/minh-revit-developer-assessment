using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System;
using System.IO;
using System.Linq;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Logic
{
    /// <summary>
    /// Tạo Opening Cut (void) tại đầu dầm để mặt cắt song song với cạnh cột.
    /// Chỉ cần dùng khi 2 dầm inline có hướng khác nhau tại cùng 1 cột.
    /// </summary>
    public class BeamEndCutter
    {

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
            // 1. Tạo/lấy void symbol
            FamilySymbol symbol = GetOrCreateVoidSymbol(doc);
            if (symbol == null) return;

            // 2. Đặt void tại cutOrigin
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

            // 3. Xoay void: +X → cutNormal (ra ngoài, cắt phần thừa của dầm)
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

            // 4. Áp dụng cut
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

                    double size = 15.0;  // 15 ft ≈ 4572mm (nửa cạnh hộp)
                    double depth = 10.0; // 10 ft ≈ 3048mm (độ sâu cắt)

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
    }
}
