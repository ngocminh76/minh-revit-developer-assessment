using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using MyRevitAddin.Core;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Logic
{
    public enum TargetType
    {
        None,
        Wall,                  // TH1
        SingleColumn,          // TH2
        InlineColumn,          // TH3
        PerpendicularBeam      // TH4
    }

    public class TargetContext
    {
        public TargetType Type { get; set; }
        public Element TargetElement { get; set; }
        public bool IsInline { get; set; }
    }

    public class BeamAdjuster
    {
        private const double MmToFeet = 1.0 / 304.8;

        private Document _doc;
        private List<FamilyInstance> _beams;
        private List<FamilyInstance> _columns;
        private List<Wall> _walls;
        private BeamEndCutter _cutter = new BeamEndCutter();

        public void AdjustBeams(Document doc, ICollection<ElementId> selectedIds, Models.AdjustBeamConfig config)
        {
            _doc = doc;
            ClassifyElements(selectedIds);

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

        private void AdjustSingleBeam(FamilyInstance beam, double wallCl, double pillarCl, double halfGap, double perpGap)
        {
            LocationCurve locCurve = beam.Location as LocationCurve;
            if (locCurve == null) return;
            Line line = locCurve.Curve as Line;
            if (line == null) return;

            XYZ start = line.GetEndPoint(0);
            XYZ end = line.GetEndPoint(1);
            XYZ beamDir = line.Direction;

            bool startNeedsVoid = false, endNeedsVoid = false;
            XYZ startCutNormal = XYZ.Zero, endCutNormal = XYZ.Zero;
            XYZ startCutOrigin = XYZ.Zero, endCutOrigin = XYZ.Zero;

            TargetContext startTarget = IdentifyTarget(beam, start, beamDir.Negate(), beamDir);
            XYZ newStart = ComputeNewEndpoint(beam, start, beamDir.Negate(), startTarget, 
                wallCl, pillarCl, halfGap, perpGap, 
                out startNeedsVoid, out startCutNormal, out startCutOrigin);

            TargetContext endTarget = IdentifyTarget(beam, end, beamDir, beamDir);
            XYZ newEnd = ComputeNewEndpoint(beam, end, beamDir, endTarget, 
                wallCl, pillarCl, halfGap, perpGap, 
                out endNeedsVoid, out endCutNormal, out endCutOrigin);

            if (newStart.DistanceTo(newEnd) > 0.01)
                locCurve.Curve = Line.CreateBound(newStart, newEnd);

            if (startTarget.IsInline && startNeedsVoid && startTarget.TargetElement is FamilyInstance)
            {
                _cutter.CutBeamEnd(_doc, beam, startCutOrigin, beamDir.Negate(), startCutNormal);
            }
            if (endTarget.IsInline && endNeedsVoid && endTarget.TargetElement is FamilyInstance)
            {
                _cutter.CutBeamEnd(_doc, beam, endCutOrigin, beamDir, endCutNormal);
            }
        }

        private TargetContext IdentifyTarget(FamilyInstance beam, XYZ endpoint, XYZ outwardDir, XYZ beamDir)
        {
            TargetContext ctx = new TargetContext { Type = TargetType.None };

            // Ưu tiên 1: CỘT
            FamilyInstance nearCol = ElementProximityUtils.FindNearestColumn(endpoint, _columns);
            if (nearCol != null)
            {
                ctx.TargetElement = nearCol;
                FamilyInstance inlineBeam = ElementProximityUtils.FindInlineBeamAtColumn(beam, nearCol, outwardDir, _beams);
                if (inlineBeam != null)
                {
                    ctx.Type = TargetType.InlineColumn;
                    ctx.IsInline = true;
                }
                else
                {
                    ctx.Type = TargetType.SingleColumn;
                }
                return ctx;
            }

            // Ưu tiên 2: TƯỜNG
            Wall nearWall = ElementProximityUtils.FindNearestWall(endpoint, _walls);
            if (nearWall != null)
            {
                ctx.TargetElement = nearWall;
                ctx.Type = TargetType.Wall;
                return ctx;
            }

            // Ưu tiên 3: DẦM VUÔNG GÓC
            FamilyInstance perpBeam = ElementProximityUtils.FindPerpendicularBeam(beam, endpoint, beamDir, _beams);
            if (perpBeam != null)
            {
                ctx.TargetElement = perpBeam;
                ctx.Type = TargetType.PerpendicularBeam;
                return ctx;
            }

            return ctx;
        }

        private XYZ ComputeNewEndpoint(FamilyInstance beam, XYZ endpoint, XYZ outwardDir, TargetContext target,
            double wallCl, double pillarCl, double halfGap, double perpGap,
            out bool needsVoidCut, out XYZ cutNormal, out XYZ cutOrigin)
        {
            needsVoidCut = false;
            cutNormal = XYZ.Zero;
            cutOrigin = XYZ.Zero;

            switch (target.Type)
            {
                case TargetType.InlineColumn:
                    return GeometryHelper.ComputeInlineEndpoint(endpoint, outwardDir, target.TargetElement as FamilyInstance, halfGap, 
                        out needsVoidCut, out cutNormal, out cutOrigin);

                case TargetType.SingleColumn:
                    return GeometryHelper.ComputeGapFromFaces(beam, endpoint, outwardDir, target.TargetElement, pillarCl);

                case TargetType.Wall:
                    return GeometryHelper.ComputeGapFromFaces(beam, endpoint, outwardDir, target.TargetElement, wallCl);

                case TargetType.PerpendicularBeam:
                    return GeometryHelper.ComputePerpBeamEndpoint(endpoint, outwardDir, target.TargetElement as FamilyInstance, perpGap);

                default:
                    return endpoint;
            }
        }
    }
}
