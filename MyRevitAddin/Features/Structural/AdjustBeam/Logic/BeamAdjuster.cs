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
                    // ▶ TH3: Hai dầm cùng phương → halfGap từ mặt cột (face-to-face)
                    return ComputeInlineEndpoint(beam, endpoint, outwardDir, nearCol, halfGap);
                }
                else
                {
                    // ▶ TH2: Dầm đơn tại cột → clearance từ mép cột
                    return ComputeColumnEndpoint(beam, endpoint, outwardDir, nearCol, pillarCl);
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

        #region TH3: Hai dầm cùng phương tại cột (Top Face Edge)

        /// <summary>
        /// Lấy mặt TOP cột → tìm cạnh mà dầm cắt ngang → offset halfGap vào trong
        /// → giao đường dầm với cạnh offset → endpoint mới.
        /// Hướng cắt theo CẠNH CỘT, không phụ thuộc hướng dầm.
        /// </summary>
        private XYZ ComputeInlineEndpoint(FamilyInstance beam, XYZ endpoint, XYZ outwardDir, FamilyInstance column, double halfGap)
        {
            XYZ colCenter = ((LocationPoint)column.Location).Point;

            // 1. Lấy mặt top cột
            PlanarFace topFace = FindTopFace(column);
            if (topFace == null)
                return ComputeGapFromFaces(beam, endpoint, outwardDir, column, halfGap);

            // 2. Tìm cạnh top face mà dầm "cắt ngang"
            //    = cạnh có pháp tuyến hướng vào (inward) trùng với outwardDir
            Line crossingEdge = null;
            XYZ edgeInwardNormal = null;
            double bestDot = -1;

            foreach (CurveLoop loop in topFace.GetEdgesAsCurveLoops())
            {
                foreach (Curve curve in loop)
                {
                    if (!(curve is Line line)) continue;
                    if (line.Length < 0.3) continue; // bỏ cạnh vát < ~90mm

                    XYZ edgeDir = line.Direction;
                    XYZ edgeMid = (line.GetEndPoint(0) + line.GetEndPoint(1)) * 0.5;

                    // Pháp tuyến hướng vào tâm cột
                    XYZ n1 = new XYZ(-edgeDir.Y, edgeDir.X, 0).Normalize();
                    XYZ toCenter = new XYZ(colCenter.X - edgeMid.X, colCenter.Y - edgeMid.Y, 0);
                    XYZ inward = n1.DotProduct(toCenter) > 0 ? n1 : n1.Negate();

                    double dot = outwardDir.DotProduct(inward);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        crossingEdge = line;
                        edgeInwardNormal = inward;
                    }
                }
            }

            if (crossingEdge == null)
                return ComputeGapFromFaces(beam, endpoint, outwardDir, column, halfGap);

            // 3. Vị trí cắt: tâm cột + halfGap hướng về phía dầm
            //    (2 dầm → 2 cut line cách tâm cột mỗi bên halfGap → gap = 2*halfGap)
            XYZ inwardDir = outwardDir.Negate();
            XYZ cutOrigin = new XYZ(
                colCenter.X + inwardDir.X * halfGap,
                colCenter.Y + inwardDir.Y * halfGap,
                endpoint.Z);
            XYZ edgeDirection = crossingEdge.Direction;

            // 4. Giao đường dầm với đường cắt (2D line-line intersection)
            //    Beam:  endpoint + t * inwardDir
            //    Cut:   cutOrigin + s * edgeDirection
            double cross2D = inwardDir.X * edgeDirection.Y - inwardDir.Y * edgeDirection.X;
            if (Math.Abs(cross2D) < 1e-10)
                return ComputeGapFromFaces(beam, endpoint, outwardDir, column, halfGap);

            double dx = cutOrigin.X - endpoint.X;
            double dy = cutOrigin.Y - endpoint.Y;
            double t = (dx * edgeDirection.Y - dy * edgeDirection.X) / cross2D;

            XYZ result = new XYZ(endpoint.X + t * inwardDir.X, endpoint.Y + t * inwardDir.Y, endpoint.Z);

            // === DEBUG LOG ===
            string logPath = @"D:\03.MINH\REVIT\RevitTest\beam_adjust_log.txt";
            try
            {
                XYZ edgeMidLog = (crossingEdge.GetEndPoint(0) + crossingEdge.GetEndPoint(1)) * 0.5;
                string log = $"\n--- TH3 TOP-EDGE: Inline tại Cột [{column.Id}] ---\n"
                    + $"  Edge Dir:          ({edgeDirection.X:F6}, {edgeDirection.Y:F6})\n"
                    + $"  Edge Midpoint:     ({edgeMidLog.X:F6}, {edgeMidLog.Y:F6})\n"
                    + $"  Inward Normal:     ({edgeInwardNormal.X:F6}, {edgeInwardNormal.Y:F6})\n"
                    + $"  outwardDir:        ({outwardDir.X:F6}, {outwardDir.Y:F6})\n"
                    + $"  halfGap:           {halfGap:F6} ft ({halfGap / MmToFeet:F1} mm)\n"
                    + $"  Endpoint (before): ({endpoint.X:F6}, {endpoint.Y:F6}, {endpoint.Z:F6})\n"
                    + $"  Endpoint (after):  ({result.X:F6}, {result.Y:F6}, {result.Z:F6})\n";
                System.IO.File.AppendAllText(logPath, log);
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Tìm mặt TOP (Normal.Z ≈ 1.0) có diện tích lớn nhất.
        /// </summary>
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
                    // Mặt top: Normal hướng lên (Z ≈ 1.0)
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


        #region Helpers: Tìm mặt phẳng đối diện dầm và tính offset
        /// <summary>
        /// Tính khoảng cách giữa 2 MẶT THỰC (mặt đầu dầm vs mặt cột),
        /// rồi dời endpoint sao cho 2 mặt cách nhau đúng clearance.
        /// </summary>
        private XYZ ComputeGapFromFaces(FamilyInstance beam, XYZ endpoint, XYZ outwardDir, Element column, double clearance)
        {
            // 1. Tìm mặt cột hướng về phía dầm
            PlanarFace colFace = FindFacingFace(column, outwardDir);
            if (colFace == null) return endpoint;

            XYZ colNormal = colFace.FaceNormal;
            XYZ colOrigin = colFace.Origin;

            // 2. Tìm mặt đầu dầm (mặt solid có normal ≈ outwardDir)
            PlanarFace beamEndFace = FindFacingFace(beam, outwardDir);

            // 3. Tính khoảng cách giữa 2 mặt thực
            //    faceDist > 0: mặt dầm nằm NGOÀI mặt cột (có khe hở)
            //    faceDist < 0: mặt dầm nằm BÊN TRONG cột (xuyên qua)
            //    faceDist = 0: 2 mặt chạm nhau
            double faceDist;
            if (beamEndFace != null)
            {
                // Khoảng cách có dấu giữa 2 mặt phẳng song song
                faceDist = (beamEndFace.Origin - colOrigin).DotProduct(colNormal);
            }
            else
            {
                // Fallback: dùng endpoint nếu không tìm được mặt dầm
                faceDist = (endpoint - colOrigin).DotProduct(colNormal);
            }

            // 4. Cần dời endpoint bao nhiêu để faceDist = -clearance?
            //    (âm vì mặt dầm phải THỤT VÀO phía tâm cột, không lòi ra ngoài)
            //    moveAmount > 0: dời ra xa cột (theo faceNormal)
            //    moveAmount < 0: dời vào gần cột (ngược faceNormal)
            double moveAmount = -clearance - faceDist;
            XYZ result = endpoint + colNormal * moveAmount;

            // === DEBUG LOG ===
            string logPath = @"D:\03.MINH\REVIT\RevitTest\beam_adjust_log.txt";
            try
            {
                string beamFaceInfo = beamEndFace != null
                    ? $"({beamEndFace.Origin.X:F6}, {beamEndFace.Origin.Y:F6}, {beamEndFace.Origin.Z:F6})"
                    : "NOT FOUND (using endpoint)";
                string log = $"\n--- FACE-TO-FACE: Dầm tại Cột/Tường [{column.Id}] ---\n"
                    + $"  Col Face Normal:   ({colNormal.X:F6}, {colNormal.Y:F6}, {colNormal.Z:F6})\n"
                    + $"  Col Face Origin:   ({colOrigin.X:F6}, {colOrigin.Y:F6}, {colOrigin.Z:F6})\n"
                    + $"  Beam Face Origin:  {beamFaceInfo}\n"
                    + $"  outwardDir:        ({outwardDir.X:F6}, {outwardDir.Y:F6}, {outwardDir.Z:F6})\n"
                    + $"  faceDist:          {faceDist:F6} ft ({faceDist / MmToFeet:F1} mm)\n"
                    + $"  clearance:         {clearance:F6} ft ({clearance / MmToFeet:F1} mm)\n"
                    + $"  moveAmount:        {moveAmount:F6} ft ({moveAmount / MmToFeet:F1} mm)\n"
                    + $"  Endpoint (before): ({endpoint.X:F6}, {endpoint.Y:F6}, {endpoint.Z:F6})\n"
                    + $"  Endpoint (after):  ({result.X:F6}, {result.Y:F6}, {result.Z:F6})\n";
                System.IO.File.AppendAllText(logPath, log);
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Tìm mặt cột/tường có pháp tuyến cùng hướng với outwardDir (hướng về phía dầm).
        /// Dùng max dot product → luôn tìm đúng mặt dù dầm bị kéo xa bao nhiêu.
        /// </summary>
        private PlanarFace FindFacingFace(Element element, XYZ outwardDir)
        {
            PlanarFace bestFace = null;
            double bestDot = -1;

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
                                PlanarFace pf = GetBestFacingFaceInSolid(s, outwardDir, ref bestDot);
                                if (pf != null) bestFace = pf;
                            }
                        }
                    }
                }
                else if (solid.Volume > 0)
                {
                    PlanarFace pf = GetBestFacingFaceInSolid(solid, outwardDir, ref bestDot);
                    if (pf != null) bestFace = pf;
                }
            }

            return bestFace;
        }

        private PlanarFace GetBestFacingFaceInSolid(Solid solid, XYZ outwardDir, ref double bestDot)
        {
            PlanarFace bestFace = null;
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace pf)
                {
                    // Bỏ qua mặt nằm ngang (đỉnh/đáy)
                    if (Math.Abs(pf.FaceNormal.Z) > 0.3) continue;
                    // Bỏ qua mặt vát mép nhỏ
                    if (pf.Area < 0.1) continue;

                    // Tìm mặt có pháp tuyến trùng hướng outwardDir nhất
                    double dot = outwardDir.DotProduct(pf.FaceNormal);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestFace = pf;
                    }
                }
            }
            return bestFace;
        }
        #endregion

        /// <summary>
        /// TH2: Dầm đơn tại cột - tính khoảng cách giữa mặt dầm và mặt cột
        /// </summary>
        private XYZ ComputeColumnEndpoint(FamilyInstance beam, XYZ endpoint, XYZ outwardDir, FamilyInstance column, double clearance)
        {
            return ComputeGapFromFaces(beam, endpoint, outwardDir, column, clearance);
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
