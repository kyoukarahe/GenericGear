using GearInvest.Core;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    public PitchPairProof ClassifyPitchClearance(PitchShape a, PitchShape b, bool required = true, PitchPairScope scope = PitchPairScope.Primitive, Rational minimumClearance = default) =>
        PitchClearanceClassifier.Classify(a, b, required, scope, minimumClearance);
    public bool VerifyPitchClearance(PitchPairProof proof, PitchShape originalA, PitchShape originalB, bool required = true, PitchPairScope scope = PitchPairScope.Primitive) =>
        PitchClearanceClassifier.Verify(proof, originalA, originalB, required, scope);
    public byte[] WritePitchClearanceProof(PitchPairProof proof) => PitchClearanceJson.WriteProof(proof);
    public PitchPairProof ReadPitchClearanceProof(byte[] bytes) => PitchClearanceJson.ReadProof(bytes);
    public PitchPairProof ClassifyPitchClearanceQuery(byte[] bytes) => PitchClearanceJson.ClassifyQuery(bytes);
}
