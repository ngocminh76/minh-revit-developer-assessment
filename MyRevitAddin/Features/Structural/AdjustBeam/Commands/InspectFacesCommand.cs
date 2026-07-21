using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class InspectFacesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Info", "Please select elements first.");
                return Result.Cancelled;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"=== INSPECT PLANAR FACES === {DateTime.Now} ===");
            sb.AppendLine($"Selected {selectedIds.Count} element(s)\n");

            foreach (var id in selectedIds)
            {
                Element el = doc.GetElement(id);
                sb.AppendLine($"══════════════════════════════════════");
                sb.AppendLine($"Element: {el.Name}");
                sb.AppendLine($"  Id:       {el.Id}");
                sb.AppendLine($"  Category: {el.Category?.Name ?? "N/A"}");

                // Location info
                if (el.Location is LocationPoint lp)
                    sb.AppendLine($"  Location: Point ({lp.Point.X:F4}, {lp.Point.Y:F4}, {lp.Point.Z:F4}) ft");
                else if (el.Location is LocationCurve lc)
                {
                    var c = lc.Curve;
                    sb.AppendLine($"  Location: Curve ({c.GetEndPoint(0).X:F4}, {c.GetEndPoint(0).Y:F4}, {c.GetEndPoint(0).Z:F4}) → ({c.GetEndPoint(1).X:F4}, {c.GetEndPoint(1).Y:F4}, {c.GetEndPoint(1).Z:F4}) ft");
                    if (c is Line line)
                        sb.AppendLine($"  Direction: ({line.Direction.X:F6}, {line.Direction.Y:F6}, {line.Direction.Z:F6})");
                }

                // BoundingBox
                BoundingBoxXYZ bb = el.get_BoundingBox(null);
                if (bb != null)
                {
                    sb.AppendLine($"  BBox Min: ({bb.Min.X:F4}, {bb.Min.Y:F4}, {bb.Min.Z:F4})");
                    sb.AppendLine($"  BBox Max: ({bb.Max.X:F4}, {bb.Max.Y:F4}, {bb.Max.Z:F4})");
                    sb.AppendLine($"  BBox Size: X={ToMm(bb.Max.X - bb.Min.X):F1}mm, Y={ToMm(bb.Max.Y - bb.Min.Y):F1}mm, Z={ToMm(bb.Max.Z - bb.Min.Z):F1}mm");
                }

                // Get Solid and iterate Faces
                var solids = GetSolids(el);
                int solidIdx = 0;
                foreach (var solid in solids)
                {
                    sb.AppendLine($"\n  --- Solid #{solidIdx} (Volume={solid.Volume:F4} ft³) ---");
                    int faceIdx = 0;
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace pf)
                        {
                            BoundingBoxUV uvBB = pf.GetBoundingBox();
                            XYZ origin = pf.Origin;
                            XYZ normal = pf.FaceNormal;

                            // Get corner points of the face
                            sb.AppendLine($"\n    [Face #{faceIdx}] PlanarFace");
                            sb.AppendLine($"      Normal:  ({normal.X:F6}, {normal.Y:F6}, {normal.Z:F6})");
                            sb.AppendLine($"      Origin:  ({origin.X:F6}, {origin.Y:F6}, {origin.Z:F6}) ft");
                            sb.AppendLine($"      Origin:  ({ToMm(origin.X):F1}, {ToMm(origin.Y):F1}, {ToMm(origin.Z):F1}) mm");
                            sb.AppendLine($"      Area:    {pf.Area:F4} ft² ({pf.Area * 92903.04:F1} mm²)");

                            // Get edge loops to find actual vertex coordinates
                            try
                            {
                                var loops = pf.GetEdgesAsCurveLoops();
                                sb.AppendLine($"      CurveLoops: {loops.Count}");
                                for (int li = 0; li < loops.Count; li++)
                                {
                                    var loop = loops[li];
                                    int curveCount = 0;
                                    foreach (var c in loop) curveCount++;
                                    sb.AppendLine($"      --- Loop #{li} ({curveCount} curves) ---");

                                    int ci = 0;
                                    foreach (Curve curve in loop)
                                    {
                                        XYZ p0 = curve.GetEndPoint(0);
                                        XYZ p1 = curve.GetEndPoint(1);
                                        double len = curve.Length;
                                        string cType = curve is Line ? "Line" : curve.GetType().Name;

                                        sb.AppendLine($"        [{ci}] {cType}  Length={len:F6} ft ({ToMm(len):F1} mm)");
                                        sb.AppendLine($"             Start: ({p0.X:F6}, {p0.Y:F6}, {p0.Z:F6}) ft  = ({ToMm(p0.X):F1}, {ToMm(p0.Y):F1}, {ToMm(p0.Z):F1}) mm");
                                        sb.AppendLine($"             End:   ({p1.X:F6}, {p1.Y:F6}, {p1.Z:F6}) ft  = ({ToMm(p1.X):F1}, {ToMm(p1.Y):F1}, {ToMm(p1.Z):F1}) mm");

                                        if (curve is Line ln)
                                        {
                                            XYZ dir = ln.Direction;
                                            sb.AppendLine($"             Dir:   ({dir.X:F6}, {dir.Y:F6}, {dir.Z:F6})");
                                        }
                                        ci++;
                                    }
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            sb.AppendLine($"\n    [Face #{faceIdx}] {face.GetType().Name} (non-planar, skipped)");
                        }
                        faceIdx++;
                    }
                    solidIdx++;
                }

                sb.AppendLine();
            }

            // Write to file
            string logPath = @"D:\03.MINH\REVIT\RevitTest\inspect_faces_log.txt";
            try
            {
                System.IO.File.WriteAllText(logPath, sb.ToString());
                Autodesk.Revit.UI.TaskDialog.Show("Done", $"Logged {selectedIds.Count} element(s) to:\n{logPath}");
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Error", $"Cannot write log: {ex.Message}");
            }

            return Result.Succeeded;
        }

        private List<Solid> GetSolids(Element element)
        {
            var result = new List<Solid>();
            var opts = new Options { DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geom = element.get_Geometry(opts);
            if (geom == null) return result;

            foreach (GeometryObject obj in geom)
            {
                if (obj is Solid s && s.Volume > 0)
                    result.Add(s);
                else if (obj is GeometryInstance gi)
                {
                    foreach (GeometryObject io in gi.GetInstanceGeometry())
                    {
                        if (io is Solid si && si.Volume > 0)
                            result.Add(si);
                    }
                }
            }
            return result;
        }

        private double ToMm(double feet) => feet * 304.8;
    }
}
