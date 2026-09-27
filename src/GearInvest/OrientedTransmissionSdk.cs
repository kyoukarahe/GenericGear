using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed class OrientedArtifactValidationResult
{
    public OrientedArtifactValidationResult(OrientedValidation validation, ArtifactIdentityVerification identity, bool storedValidationMatches)
    { Validation = validation; Identity = identity; StoredValidationMatches = storedValidationMatches; }
    public OrientedValidation Validation { get; }
    public ArtifactIdentityVerification Identity { get; }
    public bool StoredValidationMatches { get; }
    public bool IsValid => Validation.IsValid && Identity.CandidateIdMatches && Identity.ArtifactHashMatches && StoredValidationMatches;
}

public sealed partial class GearInvestSdk
{
    /// <summary>Realizes one fixed cardinal right-angle bevel pair. A requested sign is checked, never used to flip an axis.</summary>
    public OrientedTransmissionResult CreateRightAngleBevelPair(RightAngleBevelRequest request, CancellationToken token = default) => OrientedTransmissionComposer.CreatePair(request, token);
    /// <summary>Verifies source artifact bytes, then explicitly unifies supported terminal shaft ports and re-solves the whole graph.</summary>
    public OrientedTransmissionResult ComposeOrientedTransmission(OrientedAssemblyRequest request, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return OrientedTransmissionComposer.Compose(new OrientedCompositionRequest(request.Bevel), token);
        try { ValidateOrientedSources(request); return OrientedTransmissionComposer.Compose(request.ToEngineRequest(), token); }
        catch (Exception e) when (e is ArtifactFormatException || e is System.Text.Json.JsonException)
        { return new OrientedTransmissionResult(OrientedOperationStatus.InvalidInput, null, new OrientedValidation(Array.Empty<OrientedDomainCheck>(), new[] { new Diagnostic("ORIENTED_SOURCE_ARTIFACT_INVALID", DiagnosticSeverity.Error, e.Message, "sources") })); }
    }
    /// <summary>Exact independent mechanical validation; not physical tooth/shaft/bearing dynamics or manufacturing certification.</summary>
    public OrientedValidation ValidateOrientedMechanism(OrientedMechanism mechanism) => OrientedMechanismValidator.Validate(mechanism);
    /// <summary>Stateless evaluation at exact unwrapped root turns, including positive axes and world angular-rate vectors.</summary>
    public ReadOnlyCollection<OrientedShaftEvaluation> EvaluateOrientedMechanism(OrientedMechanism mechanism, Rational rootTurns) => OrientedTransmissionComposer.Evaluate(mechanism, rootTurns);
    public byte[] WriteOrientedAssemblyRequest(OrientedAssemblyRequest request) => CanonicalOrientedJson.WriteRequest(request);
    public OrientedAssemblyRequest ReadOrientedAssemblyRequest(byte[] bytes) => CanonicalOrientedJson.ReadRequest(bytes);
    /// <summary>Recomputes identities and the validation record. Invalid mechanical data remains invalid; callers must check validation before treating it as a realization.</summary>
    public OrientedArtifactWriteResult WriteOrientedArtifact(OrientedMechanism mechanism, OrientedAssemblyRequest request) => CanonicalOrientedJson.Write(mechanism, request);
    public OrientedArtifact ReadOrientedArtifact(byte[] bytes) => CanonicalOrientedJson.Read(bytes);
    public ArtifactIdentityVerification VerifyOrientedIdentity(OrientedArtifact artifact) => CanonicalOrientedJson.VerifyIdentity(artifact);
    /// <summary>Revalidates identity, all source bytes, world geometry/global solution and source/request context. Cached validation is not trusted.</summary>
    public OrientedArtifactValidationResult ValidateOrientedArtifact(OrientedArtifact artifact)
    {
        OrientedValidation validation;
        try { ValidateOrientedSources(artifact.Request); validation = OrientedTransmissionComposer.ValidateContext(artifact.Request.ToEngineRequest(), artifact.Mechanism); }
        catch (ArtifactFormatException e) { validation = new OrientedValidation(Array.Empty<OrientedDomainCheck>(), new[] { new Diagnostic("ORIENTED_SOURCE_ARTIFACT_INVALID", DiagnosticSeverity.Error, e.Message) }); }
        return new OrientedArtifactValidationResult(validation, VerifyOrientedIdentity(artifact), CanonicalOrientedJson.WriteValidation(validation).SequenceEqual(CanonicalOrientedJson.WriteValidation(artifact.StoredValidation)));
    }
    /// <summary>Executes the public composer from saved source bytes and request; compares new full canonical artifact bytes. Never uses stored result channels as answers.</summary>
    public OrientedArtifactWriteResult RebuildOrientedAssembly(OrientedArtifact artifact, CancellationToken token = default)
    {
        if (!ValidateOrientedArtifact(artifact).IsValid) throw new ArtifactFormatException("Cannot rebuild an invalid stored oriented assembly.");
        var result = ComposeOrientedTransmission(artifact.Request, token);
        if (result.Status == OrientedOperationStatus.Cancelled) throw new OperationCanceledException(token);
        if (!result.IsSuccess) throw new InvalidOperationException("Oriented request reconstruction failed: " + result.Status);
        var fresh = WriteOrientedArtifact(result.Mechanism!, artifact.Request);
        var canonicalOriginal = WriteOrientedArtifact(artifact.Mechanism, artifact.Request);
        if (fresh.Artifact.CandidateId != artifact.CandidateId || fresh.Artifact.ArtifactHash != artifact.ArtifactHash || !fresh.Bytes.SequenceEqual(canonicalOriginal.Bytes))
            throw new InvalidOperationException("Oriented fresh reconstruction drift; no replacement adopted.");
        return fresh;
    }
    /// <summary>Save As only: validate before touching the destination, then stage, read back and atomically publish a new file. No overwrite.</summary>
    public void SaveOrientedArtifact(OrientedArtifact artifact, string path)
    {
        if (!ValidateOrientedArtifact(artifact).IsValid) throw new ArtifactFormatException("Invalid oriented artifact cannot be saved as a successful assembly.");
        var bytes = WriteOrientedArtifact(artifact.Mechanism, artifact.Request).Bytes;
        var full = Path.GetFullPath(path); PortableProjectStorage.RejectLinks(full);
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException("Save As destination already exists.");
        var parent = Path.GetDirectoryName(full)!; PortableProjectStorage.RejectLinks(parent); Directory.CreateDirectory(parent);
        var pending = full + ".pending-" + Guid.NewGuid().ToString("N");
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(); }
        if (!PortableProjectStorage.ReadBounded(pending, bytes.Length).SequenceEqual(bytes)) throw new IOException("Oriented staged write readback mismatch.");
        File.Move(pending, full);
        if (!PortableProjectStorage.ReadBounded(full, bytes.Length).SequenceEqual(bytes)) throw new IOException("Oriented saved file readback mismatch.");
    }
    /// <summary>Loads a bounded external file and revalidates its declared context. This is cached Load, not request-only Rebuild.</summary>
    public OrientedArtifact LoadOrientedArtifact(string path)
    {
        var artifact = ReadOrientedArtifact(PortableProjectStorage.ReadBounded(path, CanonicalOrientedJson.MaxDocumentBytes));
        if (!ValidateOrientedArtifact(artifact).IsValid) throw new ArtifactFormatException("Oriented artifact identity/geometry/source/context validation failed.");
        return artifact;
    }
    private void ValidateOrientedSources(OrientedAssemblyRequest request)
    {
        foreach (var source in new[] { request.Upstream, request.Downstream }.Where(s => s is not null))
            if (!Validate(ReadArtifact(source!.SourceBytes)).IsValid) throw new ArtifactFormatException("Original planar source identity/mechanical context is invalid.");
    }
}
