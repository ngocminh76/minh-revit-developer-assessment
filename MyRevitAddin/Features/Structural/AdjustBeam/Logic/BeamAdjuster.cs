using Autodesk.Revit.DB;
using MyRevitAddin.Core;
using System.Collections.Generic;

namespace MyRevitAddin.Features.Structural.AdjustBeam.Logic
{
    public enum TargetType
    {
        None,
        Wall,                  // Case 1: Beam meets wall
        SingleColumn,          // Case 2: Beam meets single column
        InlineColumn,          // Case 3: Two collinear beams meet at column
        PerpendicularBeam,     // Case 4: Non-collinear beam meets perpendicular beam
        InlinePerpendicularBeam // Case 4b: Two collinear beams meet at perpendicular beam
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

            try
            {
                using (TransactionGroup tg = new TransactionGroup(doc, "Adjust Structural Beams"))
                {
                    tg.Start();

                    for (int i = 0; i < _beams.Count; i++)
                    {
                        var beam = _beams[i];

                        using (Transaction t = new Transaction(doc, "Adjust Single Beam"))
                        {
                            t.Start();
                            try
                            {
                                AdjustSingleBeam(beam, wallCl, pillarCl, halfGap, perpGap);
                            }
                            catch { }
                            t.Commit();
                        }

                        WPFUI.Utilities.ProgressDialog.ShowProgress(
                            current: i + 1,
                            total: _beams.Count,
                            message: "Adjusting structural beams...",
                            detail: $"Completed beam: {beam.Name} (ID: {beam.Id})"
                        );
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                WPFUI.Utilities.ProgressDialog.CloseProgress();
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

            // Case 3: InlineColumn -> half-space void cut
            if (startNeedsVoid && startTarget.Type == TargetType.InlineColumn && startTarget.TargetElement is FamilyInstance)
            {
                _cutter.CutBeamEnd(_doc, beam, startCutOrigin, beamDir.Negate(), startCutNormal);
            }
            if (endNeedsVoid && endTarget.Type == TargetType.InlineColumn && endTarget.TargetElement is FamilyInstance)
            {
                _cutter.CutBeamEnd(_doc, beam, endCutOrigin, beamDir, endCutNormal);
            }

            // Case 4: PerpendicularBeam -> slab void cut (only cut intersection zone)
            if (startTarget.Type == TargetType.PerpendicularBeam && startTarget.TargetElement != null)
            {
                _cutter.CutBeamPerpendicular(_doc, beam, startTarget.TargetElement, beamDir.Negate(), perpGap);
            }
            if (endTarget.Type == TargetType.PerpendicularBeam && endTarget.TargetElement != null)
            {
                _cutter.CutBeamPerpendicular(_doc, beam, endTarget.TargetElement, beamDir, perpGap);
            }

            // Case 4b: InlinePerpendicularBeam -> void cut at intersection
            if (startNeedsVoid && startTarget.Type == TargetType.InlinePerpendicularBeam)
            {
                _cutter.CutBeamEnd(_doc, beam, startCutOrigin, beamDir.Negate(), startCutNormal);
            }
            if (endNeedsVoid && endTarget.Type == TargetType.InlinePerpendicularBeam)
            {
                _cutter.CutBeamEnd(_doc, beam, endCutOrigin, beamDir, endCutNormal);
            }
        }

        private TargetContext IdentifyTarget(FamilyInstance beam, XYZ endpoint, XYZ outwardDir, XYZ beamDir)
        {
            TargetContext ctx = new TargetContext { Type = TargetType.None };

            // Priority 1: Column
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

            // Priority 2: Wall
            Wall nearWall = ElementProximityUtils.FindNearestWall(endpoint, _walls);
            if (nearWall != null)
            {
                ctx.TargetElement = nearWall;
                ctx.Type = TargetType.Wall;
                return ctx;
            }

            // Priority 3: Inline beam (collinear, end-to-end)
            FamilyInstance connectedInlineBeam = ElementProximityUtils.FindInlineBeamAtEndpoint(beam, endpoint, outwardDir, _beams);
            if (connectedInlineBeam != null)
            {
                // Check if a perpendicular beam exists at this endpoint
                FamilyInstance perpAtInline = ElementProximityUtils.FindPerpendicularBeam(beam, endpoint, beamDir, _beams);
                if (perpAtInline != null)
                {
                    // Case 4b: Two collinear beams meet AT a perpendicular beam
                    // -> Shorten by perpGap/2 and apply void cut
                    ctx.TargetElement = perpAtInline;
                    ctx.Type = TargetType.InlinePerpendicularBeam;
                    ctx.IsInline = true;
                    return ctx;
                }

                return ctx; // Return None - no perpendicular beam, skip
            }

            // Priority 4: Perpendicular beam (non-collinear beam meets perpendicular beam)
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
                    return GeometryHelper.ComputePerpBeamEndpoint(endpoint);

                case TargetType.InlinePerpendicularBeam:
                    return GeometryHelper.ComputeInlinePerpEndpoint(endpoint, outwardDir, perpGap / 2.0,
                        out needsVoidCut, out cutNormal, out cutOrigin);

                default:
                    return endpoint;
            }
        }
    }
}
