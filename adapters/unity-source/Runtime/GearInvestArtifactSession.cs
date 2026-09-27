using System;
using System.Linq;
using GearInvest.Core;
using GearInvest.Serialization.Json;

namespace GearInvest.Unity
{
    /// <summary>
    /// Immutable result of consuming one canonical artifact through the public SDK façade.
    /// </summary>
    public sealed class GearInvestArtifactSession
    {
        private readonly GearInvestSdk _sdk;
        private readonly byte[] _sourceBytes;

        private GearInvestArtifactSession(
            GearInvestSdk sdk,
            MechanismArtifact artifact,
            ArtifactValidationResult validation,
            byte[] sourceBytes)
        {
            _sdk = sdk;
            Artifact = artifact;
            Validation = validation;
            _sourceBytes = sourceBytes;
        }

        public MechanismArtifact Artifact { get; private set; }

        public ArtifactValidationResult Validation { get; private set; }

        public ArtifactIdentityVerification Identity
        {
            get { return Validation.Identity; }
        }

        public bool IsValid
        {
            get { return Validation.IsValid; }
        }

        public static GearInvestArtifactSession Load(byte[] utf8Json)
        {
            if (utf8Json == null)
            {
                throw new ArgumentNullException("utf8Json");
            }

            var sourceCopy = utf8Json.ToArray();
            var sdk = GearInvestSdk.CreateDefault();
            var artifact = sdk.ReadArtifact(sourceCopy);
            var validation = sdk.Validate(artifact);
            return new GearInvestArtifactSession(sdk, artifact, validation, sourceCopy);
        }

        public byte[] GetSourceBytes()
        {
            return _sourceBytes.ToArray();
        }

        public byte[] WriteCanonicalBytes()
        {
            return _sdk.WriteArtifact(Artifact).Bytes;
        }

        public KinematicEvaluation EvaluateExact(Rational rootTurns)
        {
            return _sdk.Evaluate(Artifact, rootTurns);
        }
    }
}
