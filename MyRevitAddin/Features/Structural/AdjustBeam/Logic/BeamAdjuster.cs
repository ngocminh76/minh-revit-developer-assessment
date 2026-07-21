using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Logic
{
    public class BeamAdjuster
    {
        private const double MmToFeet = 1.0 / 304.8;
        // Ngưỡng tìm kiếm cấu kiện lân cận (feet) ~900mm
        private const double NearbyTolerance = 3.0;

        private Document _doc;
        private List<FamilyInstance> _beams;
        private List<FamilyInstance> _columns;
        private List<Wall> _walls;

        public void AdjustBeams(Document doc, ICollection<ElementId> selectedIds, Models.AdjustBeamConfig config)
        {
            _doc = doc;
            ClassifyElements(selectedIds);

            // Chuyển đổi mm → feet
            double wallCl = config.BeamToWallClearance * MmToFeet;
            double pillarCl = config.BeamToPillarClearance * MmToFeet;
            double halfGap = (config.BeamToBeamInlineGap / 2.0) * MmToFeet;
            double perpGap = config.BeamToBeamPerpendicularGap * MmToFeet;

            using (Transaction t = new Transaction(doc, "Adjust Structural Beams"))
            {
                t.Start();

                foreach (var beam in _beams)
                {
                    try { AdjustSingleBeam(beam, wallCl, pillarCl, halfGap, perpGap); }
                    catch { }
                }

                t.Commit();
            }
        }

        #region Phân loại cấu kiện

        private void ClassifyElements(ICollection<ElementId> selectedIds)
        {
            _beams = new List<FamilyInstance>();
            _columns = new List<FamilyInstance>();
            _walls = new List<Wall>();

            foreach (var id in selectedIds)
            {
                Element el = _doc.GetElement(id);
                if (el is Wall wall)
                    _walls.Add(wall);
                else if (el is FamilyInstance fi)
                {
                    if (fi.Category.Id == new ElementId(BuiltInCategory.OST_StructuralFraming))
                        _beams.Add(fi);
                    else if (fi.Category.Id == new ElementId(BuiltInCategory.OST_StructuralColumns))
                        _columns.Add(fi);
                }
            }
        }

        #endregion

        #region Xử lý từng dầm

        private void AdjustSingleBeam(FamilyInstance beam, double wallCl, double pillarCl, double halfGap, double perpGap)
        {
            LocationCurve locCurve = beam.Location as LocationCurve;
            if (locCurve == null) return;
            Line line = locCurve.Curve as Line;
            if (line == null) return;

            XYZ start = line.GetEndPoint(0);
            XYZ end = line.GetEndPoint(1);
            XYZ beamDir = line.Direction;

            XYZ newStart = ComputeNewEndpoint(beam, start, beamDir.Negate(), beamDir,
                wallCl, pillarCl, halfGap, perpGap);
            XYZ newEnd = ComputeNewEndpoint(beam, end, beamDir, beamDir,
                wallCl, pillarCl, halfGap, perpGap);

            if (newStart.DistanceTo(newEnd) > 0.01)
                locCurve.Curve = Line.CreateBound(newStart, newEnd);
        }

        /// <summary>
        /// Tính vị trí mới cho 1 đầu dầm.
        /// outwardDir: hướng từ thân dầm ra đầu mút.
        /// </summary>
        private XYZ ComputeNewEndpoint(FamilyInstance beam, XYZ endpoint, XYZ outwardDir, XYZ beamDir,
            double wallCl, double pillarCl, double halfGap, double perpGap)
        {
            XYZ inwardDir = outwardDir.Negate();

            // ═══════ Ưu tiên 1: CỘT ═══════
            FamilyInstance nearCol = FindNearestColumn(endpoint);
            if (nearCol != null)
            {
                FamilyInstance inlineBeam = FindInlineBeamAtColumn(beam, nearCol, outwardDir);
                if (inlineBeam != null)
                {
                    // ▶ TH3: Hai dầm cùng phương → halfGap từ tâm cột
                    return ComputeInlineEndpoint(endpoint, outwardDir, nearCol, halfGap);
                }
                else
                {
                    // ▶ TH2: Dầm đơn tại cột → clearance từ mép cột
                    return ComputeColumnEndpoint(endpoint, outwardDir, nearCol, pillarCl);
                }
            }

            // ═══════ Ưu tiên 2: TƯỜNG ═══════
            Wall nearWall = FindNearestWall(endpoint);
            if (nearWall != null)
            {
                // ▶ TH1: Dầm tại tường → clearance từ mép tường
                return ComputeWallEndpoint(endpoint, outwardDir, nearWall, wallCl);
            }

            // ═══════ Ưu tiên 3: DẦM VUÔNG GÓC ═══════
            FamilyInstance perpBeam = FindPerpendicularBeam(beam, endpoint, beamDir);
            if (perpBeam != null)
            {
                // ▶ TH4: Dầm vuông góc → clearance từ mép thân dầm kia
                return ComputePerpBeamEndpoint(endpoint, outwardDir, perpBeam, perpGap);
            }

            return endpoint;
        }

        #endregion

        #region TH3: Hai dầm cùng phương tại cột (TOÁN HỌC)

        /// <summary>
        /// Mỗi đầu dầm cách tâm cột = halfGap (dọc theo trục dầm).
        /// </summary>
        private XYZ ComputeInlineEndpoint(XYZ endpoint, XYZ outwardDir, FamilyInstance column, double halfGap)
        {
            XYZ inwardDir = outwardDir.Negate();
            XYZ colCenter = ((LocationPoint)column.Location).Point;
            XYZ colOnLine = ProjectPointOnLine(colCenter, endpoint, outwardDir);
            return colOnLine + inwardDir * halfGap;
        }

        #endregion


        #region Helpers: Tìm mặt phẳng gần nhất và tính offset
        private XYZ ComputeGapFromClosestFace(XYZ endpoint, XYZ outwardDir, Element element, double clearance)
        {
            PlanarFace closestFace = FindClosestFace(element, endpoint);
            if (closestFace == null) return endpoint;

            XYZ faceNormal = closestFace.FaceNormal;
            XYZ faceOrigin = closestFace.Origin;

            // 1. Chiếu endpoint hiện tại lên mặt phẳng (để triệt tiêu sai số dầm vẽ lố/hụt)
            double distToPlane = (endpoint - faceOrigin).DotProduct(faceNormal);
            XYZ projectedPt = endpoint - distToPlane * faceNormal;

            // 2. Hướng cắt ngắn dầm (hướng vào giữa thân dầm)
            XYZ inwardDir = outwardDir.Negate();

            // 3. Tính quãng đường cần dời dọc theo inwardDir để có khoảng cách vuông góc = clearance
            double dot = Math.Abs(inwardDir.DotProduct(faceNormal));
            if (dot < 0.001) dot = 1.0; // Tránh lỗi chia 0
            
            double moveDist = clearance / dot;

            // 4. Dời điểm đã chiếu vào trong thân dầm
            XYZ result = projectedPt + inwardDir * moveDist;

            // === DEBUG LOG ===
            string logPath = @"D:\03.MINH\REVIT\RevitTest\beam_adjust_log.txt";
            try
            {
                string log = $"\n--- NEW LOGIC: Dầm tại Cột/Tường [{element.Id}] ---\n"
                    + $"  Face Normal:       ({faceNormal.X:F6}, {faceNormal.Y:F6}, {faceNormal.Z:F6})\n"
                    + $"  Face Origin:       ({faceOrigin.X:F6}, {faceOrigin.Y:F6}, {faceOrigin.Z:F6})\n"
                    + $"  outwardDir:        ({outwardDir.X:F6}, {outwardDir.Y:F6}, {outwardDir.Z:F6})\n"
                    + $"  distToPlane:       {distToPlane:F6} ft ({distToPlane / MmToFeet:F1} mm)\n"
                    + $"  clearance:         {clearance:F6} ft ({clearance / MmToFeet:F1} mm)\n"
                    + $"  moveDist:          {moveDist:F6} ft ({moveDist / MmToFeet:F1} mm)\n"
                    + $"  Endpoint (before): ({endpoint.X:F6}, {endpoint.Y:F6}, {endpoint.Z:F6})\n"
                    + $"  Endpoint (after):  ({result.X:F6}, {result.Y:F6}, {result.Z:F6})\n";
                System.IO.File.AppendAllText(logPath, log);
            }
            catch { }

            return result;
        }

        private PlanarFace FindClosestFace(Element element, XYZ endpoint)
        {
            PlanarFace bestFace = null;
            double minDistance = double.MaxValue;

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
                                PlanarFace pf = GetClosestFaceInSolid(s, endpoint, ref minDistance);
                                if (pf != null) bestFace = pf;
                            }
                        }
                    }
                }
                else if (solid.Volume > 0)
                {
                    PlanarFace pf = GetClosestFaceInSolid(solid, endpoint, ref minDistance);
                    if (pf != null) bestFace = pf;
                }
            }

            return bestFace;
        }

        private PlanarFace GetClosestFaceInSolid(Solid solid, XYZ endpoint, ref double minDistance)
        {
            PlanarFace bestFace = null;
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace pf)
                {
                    // Bỏ qua mặt nằm ngang (đỉnh/đáy cột/tường)
                    if (Math.Abs(pf.FaceNormal.Z) > 0.3) continue;
                    // Bỏ qua các mặt quá nhỏ (ví dụ: vát mép 10mm -> area rất nhỏ)
                    if (pf.Area < 0.1) continue; 

                    // Khoảng cách vuông góc từ endpoint tới mặt phẳng
                    double dist = Math.Abs((endpoint - pf.Origin).DotProduct(pf.FaceNormal));
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        bestFace = pf;
                    }
                }
            }
            return bestFace;
        }
        #endregion

        /// <summary>
        /// Dùng phương pháp tìm mặt gần nhất để tính offset
        /// </summary>
        private XYZ ComputeColumnEndpoint(XYZ endpoint, XYZ outwardDir, FamilyInstance column, double clearance)
        {
            return ComputeGapFromClosestFace(endpoint, outwardDir, column, clearance);
        }

        #region TH1: Dầm tại tường (TOÁN HỌC - Mặt phẳng)

        /// <summary>
        /// Tính mép tường bằng mặt phẳng (centerLine ± width/2).
        /// Giao đường dầm với mặt phẳng tường → facePoint.
        /// ĐầuDầmMới = facePoint + inwardDir * clearance
        /// </summary>
        private XYZ ComputeWallEndpoint(XYZ endpoint, XYZ outwardDir, Wall wall, double clearance)
        {
            XYZ inwardDir = outwardDir.Negate();

            LocationCurve wallLoc = wall.Location as LocationCurve;
            if (wallLoc == null) return endpoint;
            Line wallLine = wallLoc.Curve as Line;
            if (wallLine == null) return endpoint;

            XYZ wallDir = wallLine.Direction;
            // Pháp tuyến tường (vuông góc với hướng tường, trong mặt phẳng XY)
            XYZ wallNormal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();
            double halfWidth = wall.Width / 2.0;

            XYZ wallOrigin = wallLine.GetEndPoint(0);

            // Khoảng cách có dấu từ endpoint đến mặt phẳng trung tâm tường
            double distToCenter = (endpoint - wallOrigin).DotProduct(wallNormal);

            // Xác định mặt tường phía dầm tiếp cận
            double inwardDotNormal = inwardDir.DotProduct(wallNormal);
            if (Math.Abs(inwardDotNormal) < 1e-10) return endpoint; // dầm song song tường

            double faceOffset = Math.Sign(inwardDotNormal) * halfWidth;

            // Tìm giao điểm đường dầm với mặt phẳng tường
            double outwardDotNormal = outwardDir.DotProduct(wallNormal);
            if (Math.Abs(outwardDotNormal) < 1e-10) return endpoint;

            double t = (distToCenter - faceOffset) / outwardDotNormal;
            XYZ facePoint = endpoint - outwardDir * t;

            return facePoint + inwardDir * clearance;
        }

        #endregion

        #region TH4: Dầm vuông góc (TOÁN HỌC)

        /// <summary>
        /// Tính mép thân dầm vuông góc (coi như "tường mỏng").
        /// Dùng tâm đường dầm kia + nửa chiều rộng tiết diện.
        /// </summary>
        private XYZ ComputePerpBeamEndpoint(XYZ endpoint, XYZ outwardDir, FamilyInstance perpBeam, double clearance)
        {
            XYZ inwardDir = outwardDir.Negate();

            LocationCurve perpLoc = perpBeam.Location as LocationCurve;
            if (perpLoc == null) return endpoint;
            Line perpLine = perpLoc.Curve as Line;
            if (perpLine == null) return endpoint;

            XYZ perpDir = perpLine.Direction;

            // Điểm gần nhất trên trục dầm vuông góc → tâm tiết diện tại vị trí giao
            XYZ closestOnPerp = ClosestOnSegment(endpoint, perpLine.GetEndPoint(0), perpLine.GetEndPoint(1));

            // Chiếu tâm tiết diện lên đường dầm hiện tại
            XYZ centerOnOurLine = ProjectPointOnLine(closestOnPerp, endpoint, outwardDir);

            // Ước lượng nửa chiều rộng tiết diện dầm vuông góc:
            // Lấy BBox, chiều nhỏ nhất trong XY chính là bề rộng tiết diện (b)
            BoundingBoxXYZ bb = perpBeam.get_BoundingBox(null);
            if (bb == null) return endpoint;

            double dimX = bb.Max.X - bb.Min.X;
            double dimY = bb.Max.Y - bb.Min.Y;

            // Chiều dài dầm vuông góc (chiều lớn nhất XY) ≠ tiết diện
            // Chiều nhỏ nhất XY = bề rộng tiết diện (b)
            double crossSectionWidth = Math.Min(dimX, dimY);
            double halfCrossWidth = crossSectionWidth / 2.0;

            // Đầu dầm mới = tâm dầm vuông góc - (halfCrossWidth + clearance)
            return centerOnOurLine + inwardDir * (halfCrossWidth + clearance);
        }

        #endregion

        #region Tìm cấu kiện lân cận

        private FamilyInstance FindNearestColumn(XYZ point)
        {
            FamilyInstance nearest = null;
            double minDist = NearbyTolerance;
            foreach (var col in _columns)
            {
                var lp = col.Location as LocationPoint;
                if (lp == null) continue;
                double dist = Dist2D(point, lp.Point);
                if (dist < minDist) { minDist = dist; nearest = col; }
            }
            return nearest;
        }

        private Wall FindNearestWall(XYZ point)
        {
            Wall nearest = null;
            double minDist = NearbyTolerance;
            foreach (var wall in _walls)
            {
                var wlc = wall.Location as LocationCurve;
                if (wlc == null) continue;
                var wl = wlc.Curve as Line;
                if (wl == null) continue;
                double dist = DistToSegment2D(point, wl.GetEndPoint(0), wl.GetEndPoint(1));
                if (dist < minDist) { minDist = dist; nearest = wall; }
            }
            return nearest;
        }

        private FamilyInstance FindInlineBeamAtColumn(FamilyInstance thisBeam, FamilyInstance column, XYZ outwardDir)
        {
            XYZ colCenter = ((LocationPoint)column.Location).Point;
            foreach (var other in _beams)
            {
                if (other.Id == thisBeam.Id) continue;
                var olc = other.Location as LocationCurve;
                if (olc == null) continue;
                var ol = olc.Curve as Line;
                if (ol == null) continue;
                // Song song: |cos(angle)| > cos(15°)
                if (Math.Abs(outwardDir.DotProduct(ol.Direction)) < 0.966) continue;
                // Dầm kia có đầu gần cột
                if (Dist2D(ol.GetEndPoint(0), colCenter) < NearbyTolerance ||
                    Dist2D(ol.GetEndPoint(1), colCenter) < NearbyTolerance)
                    return other;
            }
            return null;
        }

        private FamilyInstance FindPerpendicularBeam(FamilyInstance thisBeam, XYZ endpoint, XYZ beamDir)
        {
            FamilyInstance nearest = null;
            double minDist = NearbyTolerance;
            foreach (var other in _beams)
            {
                if (other.Id == thisBeam.Id) continue;
                var olc = other.Location as LocationCurve;
                if (olc == null) continue;
                var ol = olc.Curve as Line;
                if (ol == null) continue;
                // Vuông góc: |cos(angle)| < cos(75°) ≈ 0.259
                if (Math.Abs(beamDir.DotProduct(ol.Direction)) > 0.259) continue;
                XYZ closest = ClosestOnSegment(endpoint, ol.GetEndPoint(0), ol.GetEndPoint(1));
                double dist = endpoint.DistanceTo(closest);
                if (dist < minDist) { minDist = dist; nearest = other; }
            }
            return nearest;
        }

        #endregion

        #region Hàm toán học

        /// <summary>Chiếu điểm P lên đường thẳng đi qua lineOrigin theo hướng lineDir.</summary>
        private XYZ ProjectPointOnLine(XYZ point, XYZ lineOrigin, XYZ lineDir)
        {
            XYZ d = lineDir.Normalize();
            return lineOrigin + d * (point - lineOrigin).DotProduct(d);
        }

        /// <summary>Điểm gần nhất trên đoạn AB đến P (3D).</summary>
        private XYZ ClosestOnSegment(XYZ p, XYZ a, XYZ b)
        {
            XYZ ab = b - a;
            double len = ab.GetLength();
            if (len < 1e-10) return a;
            XYZ dir = ab / len;
            double t = Math.Max(0, Math.Min(len, (p - a).DotProduct(dir)));
            return a + dir * t;
        }

        /// <summary>Khoảng cách 2D (XY) giữa 2 điểm.</summary>
        private double Dist2D(XYZ a, XYZ b)
        {
            return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        }

        /// <summary>Khoảng cách 2D (XY) từ điểm đến đoạn thẳng.</summary>
        private double DistToSegment2D(XYZ p, XYZ a, XYZ b)
        {
            XYZ p2 = new XYZ(p.X, p.Y, 0);
            XYZ a2 = new XYZ(a.X, a.Y, 0);
            XYZ b2 = new XYZ(b.X, b.Y, 0);
            return p2.DistanceTo(ClosestOnSegment(p2, a2, b2));
        }

        #endregion
    }
}
