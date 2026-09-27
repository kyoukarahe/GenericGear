using UnityEngine;

namespace GearInvest.Unity
{
    public sealed class GearInvestBodyView : MonoBehaviour
    {
        public string BodyId { get; private set; }

        public string DofId { get; private set; }

        public string AxisId { get; private set; }

        public int Layer { get; private set; }

        public int ToothCount { get; private set; }

        public double PitchRadius { get; private set; }

        public double LastAppliedTurns { get; private set; }

        internal void Initialize(
            string bodyId,
            string dofId,
            string axisId,
            int layer,
            int toothCount,
            double pitchRadius)
        {
            BodyId = bodyId;
            DofId = dofId;
            AxisId = axisId;
            Layer = layer;
            ToothCount = toothCount;
            PitchRadius = pitchRadius;
            name = "Body " + bodyId;
        }

        internal void ApplyTurns(double turns)
        {
            LastAppliedTurns = turns;
            transform.localRotation = Quaternion.AngleAxis((float)(turns * 360.0), Vector3.up);
        }
    }
}
