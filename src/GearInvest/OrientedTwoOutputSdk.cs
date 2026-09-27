using System;
using System.IO;
using System.Linq;
using System.Threading;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    /// <summary>One bevel input driver, a parallel chain on that same input shaft, and an optional turned chain. No routing or search.</summary>
    public OrientedTwoOutputResult ComposeOrientedTwoOutput(OrientedTwoOutputAssemblyRequest request, CancellationToken token = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (token.IsCancellationRequested) return new OrientedTwoOutputResult(OrientedOperationStatus.Cancelled, null, SourceIssue("Cancelled before source read.", request.Profile));
        try { ValidateTwoOutputSources(request); return OrientedTwoOutputComposer.Compose(request.ToEngineRequest(), token); }
        catch (ArtifactFormatException e) { return new OrientedTwoOutputResult(OrientedOperationStatus.InvalidInput, null, SourceIssue(e.Message, request.Profile)); }
    }
    public OrientedValidation ValidateOrientedTwoOutputMechanism(OrientedTwoOutputMechanism mechanism) => OrientedTwoOutputValidator.Validate(mechanism);
    public OrientedTwoOutputEvaluation EvaluateOrientedTwoOutput(OrientedTwoOutputMechanism mechanism, Rational rootTurns) => OrientedTwoOutputComposer.Evaluate(mechanism, rootTurns);
    public byte[] WriteOrientedTwoOutputRequest(OrientedTwoOutputAssemblyRequest request) => CanonicalOrientedTwoOutputJson.WriteRequest(request);
    public OrientedTwoOutputAssemblyRequest ReadOrientedTwoOutputRequest(byte[] bytes) => CanonicalOrientedTwoOutputJson.ReadRequest(bytes);
    /// <summary>Hashes and records fresh validation; identity is not a mechanical validity claim.</summary>
    public OrientedTwoOutputArtifactWriteResult WriteOrientedTwoOutputArtifact(OrientedTwoOutputMechanism mechanism, OrientedTwoOutputAssemblyRequest request) => CanonicalOrientedTwoOutputJson.Write(mechanism, request);
    public OrientedTwoOutputArtifact ReadOrientedTwoOutputArtifact(byte[] bytes) => CanonicalOrientedTwoOutputJson.Read(bytes);
    public ArtifactIdentityVerification VerifyOrientedTwoOutputIdentity(OrientedTwoOutputArtifact artifact) => CanonicalOrientedTwoOutputJson.VerifyIdentity(artifact);
    public OrientedArtifactValidationResult ValidateOrientedTwoOutputArtifact(OrientedTwoOutputArtifact artifact)
    {
        OrientedValidation validation;
        try { ValidateTwoOutputSources(artifact.Request); validation = OrientedTwoOutputValidator.ValidateContext(artifact.Request.ToEngineRequest(), artifact.Mechanism); }
        catch (ArtifactFormatException e) { validation = SourceIssue(e.Message, artifact.Request.Profile); }
        return new OrientedArtifactValidationResult(validation, VerifyOrientedTwoOutputIdentity(artifact), PitchClearanceJson.WriteValidation(validation).SequenceEqual(PitchClearanceJson.WriteValidation(artifact.StoredValidation)));
    }
    /// <summary>Request-only realization from original source bytes; cached Load and fresh Rebuild are separate operations.</summary>
    public OrientedTwoOutputArtifactWriteResult RebuildOrientedTwoOutput(OrientedTwoOutputArtifact artifact, CancellationToken token = default)
    {
        if (!ValidateOrientedTwoOutputArtifact(artifact).IsValid) throw new ArtifactFormatException("Cannot rebuild an invalid two-output artifact.");
        var result = ComposeOrientedTwoOutput(artifact.Request, token);
        if (result.Status == OrientedOperationStatus.Cancelled) throw new OperationCanceledException(token);
        if (!result.IsSuccess) throw new InvalidOperationException("Request-only two-output reconstruction failed: " + result.Status);
        var fresh = WriteOrientedTwoOutputArtifact(result.Mechanism!, artifact.Request);
        var original = WriteOrientedTwoOutputArtifact(artifact.Mechanism, artifact.Request);
        if (fresh.Artifact.CandidateId != artifact.CandidateId || fresh.Artifact.ArtifactHash != artifact.ArtifactHash || !fresh.Bytes.SequenceEqual(original.Bytes))
            throw new InvalidOperationException("Fresh two-output reconstruction differs; no replacement adopted.");
        return fresh;
    }
    /// <summary>Validate and atomically Save As a new file; never overwrite a prior artifact.</summary>
    public void SaveOrientedTwoOutputArtifact(OrientedTwoOutputArtifact artifact, string path)
    {
        if (!ValidateOrientedTwoOutputArtifact(artifact).IsValid) throw new ArtifactFormatException("Invalid two-output artifact cannot be saved as a successful assembly.");
        var bytes = WriteOrientedTwoOutputArtifact(artifact.Mechanism, artifact.Request).Bytes;
        var full = Path.GetFullPath(path); PortableProjectStorage.RejectLinks(full);
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException("Save As destination already exists.");
        var parent = Path.GetDirectoryName(full)!; PortableProjectStorage.RejectLinks(parent); Directory.CreateDirectory(parent);
        var pending = full + ".pending-" + Guid.NewGuid().ToString("N");
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(); }
        if (!PortableProjectStorage.ReadBounded(pending, bytes.Length).SequenceEqual(bytes)) throw new IOException("Two-output staged write readback mismatch.");
        File.Move(pending, full);
        if (!PortableProjectStorage.ReadBounded(full, bytes.Length).SequenceEqual(bytes)) throw new IOException("Two-output saved readback mismatch.");
    }
    public OrientedTwoOutputArtifact LoadOrientedTwoOutputArtifact(string path)
    {
        var artifact = ReadOrientedTwoOutputArtifact(PortableProjectStorage.ReadBounded(path, CanonicalOrientedTwoOutputJson.MaxDocumentBytes));
        if (!ValidateOrientedTwoOutputArtifact(artifact).IsValid) throw new ArtifactFormatException("Two-output identity/geometry/source/context validation failed.");
        return artifact;
    }
    private void ValidateTwoOutputSources(OrientedTwoOutputAssemblyRequest request)
    {
        if (request.ParallelBranch is null) throw new ArtifactFormatException("Mandatory parallel source absent.");
        foreach (var source in new[] { request.ParallelBranch, request.TurnedBranch }.Where(s => s is not null))
            if (!Validate(ReadArtifact(source!.SourceBytes)).IsValid) throw new ArtifactFormatException("Original planar source identity/mechanical context is invalid.");
    }
    private static OrientedValidation SourceIssue(string message, string profile) => new(Array.Empty<OrientedDomainCheck>(),
        new[] { new Diagnostic("ORIENTED_TWO_OUTPUT_SOURCE_INVALID", DiagnosticSeverity.Error, message, "sources") },
        clearancePolicy: profile == OrientedTwoOutputProfile.RefinedId ? PitchClearancePolicy.Refined : PitchClearancePolicy.Legacy);
}
