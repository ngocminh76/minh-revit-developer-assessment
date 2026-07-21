namespace MyRevitAddin.Features.Structural.AdjustBeam.Models
{
    public class AdjustBeamConfig
    {
        public double BeamToWallClearance { get; set; } = 20.0;
        public double BeamToPillarClearance { get; set; } = 20.0;
        public double BeamToBeamInlineGap { get; set; } = 20.0;
        public double BeamToBeamPerpendicularGap { get; set; } = 20.0;

        public string BeamCornerAtPillar { get; set; } = "Default";
        public bool CornerTShapeAtPillarExtendToBeamBody { get; set; } = true;
        public bool CornerAtWallExtendToBeamBody { get; set; } = true;
    }
}
