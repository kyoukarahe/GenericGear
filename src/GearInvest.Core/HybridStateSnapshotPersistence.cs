using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GearInvest.Core;

public static class HybridStateSnapshotDocumentContract
{
    public const string Format = "gear-invest.hybrid-state-snapshot";
    public const string FormatVersion = "0.1";
}

public enum HybridStateSnapshotAuthorityKind
{
    Final,
    Checkpoint,
}

public sealed class HybridStateSnapshotAuthority
{
    public HybridStateSnapshotAuthority(
        HybridStateSnapshotAuthorityKind kind,
        string compatibleTransitionPlanId,
        string determinismProfile)
    {
        Kind = kind;
        CompatibleTransitionPlanId = Require(compatibleTransitionPlanId, nameof(compatibleTransitionPlanId));
        DeterminismProfile = Require(determinismProfile, nameof(determinismProfile));
    }

    public HybridStateSnapshotAuthorityKind Kind { get; }
    public string CompatibleTransitionPlanId { get; }
    public string DeterminismProfile { get; }

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty snapshot authority value is required.", parameterName)
            : value;
}

public sealed class HybridStateSnapshotProvenance
{
    public HybridStateSnapshotProvenance(string sourceResultFormat, string sourceResultRequestId)
    {
        SourceResultFormat = Require(sourceResultFormat, nameof(sourceResultFormat));
        SourceResultRequestId = Require(sourceResultRequestId, nameof(sourceResultRequestId));
    }

    public string SourceResultFormat { get; }
    public string SourceResultRequestId { get; }

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty snapshot provenance value is required.", parameterName)
            : value;
}

public sealed class HybridStateSnapshotDocument
{
    public HybridStateSnapshotDocument(
        HybridStateSnapshotAuthority authority,
        HybridStateSnapshot snapshot,
        HybridStateSnapshotProvenance? provenance = null,
        string? storedHybridStateSnapshotId = null)
    {
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Provenance = provenance;
        StoredHybridStateSnapshotId = string.IsNullOrWhiteSpace(storedHybridStateSnapshotId)
            ? snapshot.HybridStateSnapshotId
            : storedHybridStateSnapshotId;
    }

    public string Format => HybridStateSnapshotDocumentContract.Format;
    public string FormatVersion => HybridStateSnapshotDocumentContract.FormatVersion;
    public HybridStateSnapshotAuthority Authority { get; }
    public HybridStateSnapshot Snapshot { get; }
    public HybridStateSnapshotProvenance? Provenance { get; }
    public string StoredHybridStateSnapshotId { get; }
}

public sealed class HybridStateSnapshotIdentityVerification
{
    public HybridStateSnapshotIdentityVerification(string storedSnapshotId, string computedSnapshotId)
    {
        StoredSnapshotId = storedSnapshotId ?? string.Empty;
        ComputedSnapshotId = computedSnapshotId ?? string.Empty;
    }

    public string StoredSnapshotId { get; }
    public string ComputedSnapshotId { get; }
    public bool Matches => StringComparer.Ordinal.Equals(StoredSnapshotId, ComputedSnapshotId);
}

public enum HybridStateSnapshotReadStatus
{
    Success,
    InvalidInput,
    Unsupported,
}

public sealed class HybridStateSnapshotReadResult
{
    public HybridStateSnapshotReadResult(
        HybridStateSnapshotReadStatus status,
        HybridStateSnapshotDocument? document,
        IEnumerable<Diagnostic> diagnostics)
    {
        Status = status;
        Document = document;
        Diagnostics = DiagnosticOrdering.Canonicalize(
            diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)));
    }

    public HybridStateSnapshotReadStatus Status { get; }
    public HybridStateSnapshotDocument? Document { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsSuccess => Status == HybridStateSnapshotReadStatus.Success && Document is not null &&
        Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

public enum HybridStateSnapshotValidationStatus
{
    Valid,
    InvalidInput,
    Unsupported,
}

public sealed class HybridStateSnapshotValidationResult
{
    public HybridStateSnapshotValidationResult(
        HybridStateSnapshotValidationStatus status,
        IEnumerable<Diagnostic> diagnostics)
    {
        Status = status;
        Diagnostics = DiagnosticOrdering.Canonicalize(
            diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)));
    }

    public HybridStateSnapshotValidationStatus Status { get; }
    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }
    public bool IsValid => Status == HybridStateSnapshotValidationStatus.Valid &&
        Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}
