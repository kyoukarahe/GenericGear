using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using GearInvest.Core;

namespace GearInvest.Modules.Clock;

public static class ClockSemanticContract
{
    public const string SourceKind = "clock-display-recipe";
    public const string CompilerId = "clock-periodic-recipe-compiler";
    public const string CompilerVersion = "1";
    public const string DefaultDeterminismProfile = PeriodicSemanticContract.DefaultDeterminismProfile;
}

public enum ClockDisplayRole
{
    Seconds,
    Minutes,
    Hours12,
    Day24,
}

public sealed class ClockDisplayDefinition
{
    public ClockDisplayDefinition(
        string semanticNodeId,
        ClockDisplayRole role,
        Rational exactPeriodSeconds,
        Rational exactInitialPhaseTurns)
    {
        SemanticNodeId = semanticNodeId ?? string.Empty;
        Role = role;
        ExactPeriodSeconds = exactPeriodSeconds;
        ExactInitialPhaseTurns = exactInitialPhaseTurns;
    }

    public string SemanticNodeId { get; }
    public ClockDisplayRole Role { get; }
    public Rational ExactPeriodSeconds { get; }
    public Rational ExactInitialPhaseTurns { get; }
}

public sealed class ClockDisplayConnection
{
    public ClockDisplayConnection(
        string id,
        string fromSemanticNodeId,
        string toSemanticNodeId,
        RotationDirection requiredDirection,
        Rational exactPhaseRelation,
        Rational? optionalExplicitTransfer = null)
    {
        Id = id ?? string.Empty;
        FromSemanticNodeId = fromSemanticNodeId ?? string.Empty;
        ToSemanticNodeId = toSemanticNodeId ?? string.Empty;
        RequiredDirection = requiredDirection;
        ExactPhaseRelation = exactPhaseRelation;
        OptionalExplicitTransfer = optionalExplicitTransfer;
    }

    public string Id { get; }
    public string FromSemanticNodeId { get; }
    public string ToSemanticNodeId { get; }
    public RotationDirection RequiredDirection { get; }
    public Rational ExactPhaseRelation { get; }
    public Rational? OptionalExplicitTransfer { get; }
}

public sealed class ClockDisplayRecipe
{
    public ClockDisplayRecipe(
        string catalogId,
        IEnumerable<ClockDisplayDefinition> displays,
        IEnumerable<ClockDisplayConnection> connections)
    {
        CatalogId = catalogId ?? string.Empty;
        Displays = (displays ?? throw new ArgumentNullException(nameof(displays))).ToList().AsReadOnly();
        Connections = (connections ?? throw new ArgumentNullException(nameof(connections))).ToList().AsReadOnly();
    }

    public string CatalogId { get; }
    public ReadOnlyCollection<ClockDisplayDefinition> Displays { get; }
    public ReadOnlyCollection<ClockDisplayConnection> Connections { get; }
}

public static class SecondsMinutesHours12Recipe
{
    public const string CatalogId = "seconds-minutes-hours12";
    public const string SecondsNodeId = "seconds";
    public const string MinutesNodeId = "minutes";
    public const string Hours12NodeId = "hours12";
    public const string SecondsToMinutesRequirementId = "seconds-to-minutes";
    public const string MinutesToHours12RequirementId = "minutes-to-hours12";

    public static ClockDisplayRecipe Create()
    {
        return new ClockDisplayRecipe(
            CatalogId,
            new[]
            {
                new ClockDisplayDefinition(SecondsNodeId, ClockDisplayRole.Seconds, new Rational(60), Rational.Zero),
                new ClockDisplayDefinition(MinutesNodeId, ClockDisplayRole.Minutes, new Rational(3600), Rational.Zero),
                new ClockDisplayDefinition(Hours12NodeId, ClockDisplayRole.Hours12, new Rational(43200), Rational.Zero),
            },
            new[]
            {
                new ClockDisplayConnection(
                    SecondsToMinutesRequirementId,
                    SecondsNodeId,
                    MinutesNodeId,
                    RotationDirection.Same,
                    Rational.Zero),
                new ClockDisplayConnection(
                    MinutesToHours12RequirementId,
                    MinutesNodeId,
                    Hours12NodeId,
                    RotationDirection.Same,
                    Rational.Zero),
            });
    }
}

public static class SecondsMinutesHours12Day24Recipe
{
    public const string CatalogId = "seconds-minutes-hours12-day24";
    public const string SecondsNodeId = SecondsMinutesHours12Recipe.SecondsNodeId;
    public const string MinutesNodeId = SecondsMinutesHours12Recipe.MinutesNodeId;
    public const string Hours12NodeId = SecondsMinutesHours12Recipe.Hours12NodeId;
    public const string Day24NodeId = "day24";
    public const string SecondsToMinutesRequirementId = SecondsMinutesHours12Recipe.SecondsToMinutesRequirementId;
    public const string MinutesToHours12RequirementId = SecondsMinutesHours12Recipe.MinutesToHours12RequirementId;
    public const string MinutesToDay24RequirementId = "minutes-to-day24";

    public static ClockDisplayRecipe Create()
    {
        return new ClockDisplayRecipe(
            CatalogId,
            new[]
            {
                new ClockDisplayDefinition(SecondsNodeId, ClockDisplayRole.Seconds, new Rational(60), Rational.Zero),
                new ClockDisplayDefinition(MinutesNodeId, ClockDisplayRole.Minutes, new Rational(3600), Rational.Zero),
                new ClockDisplayDefinition(Hours12NodeId, ClockDisplayRole.Hours12, new Rational(43200), Rational.Zero),
                new ClockDisplayDefinition(Day24NodeId, ClockDisplayRole.Day24, new Rational(86400), Rational.Zero),
            },
            new[]
            {
                new ClockDisplayConnection(
                    SecondsToMinutesRequirementId,
                    SecondsNodeId,
                    MinutesNodeId,
                    RotationDirection.Same,
                    Rational.Zero),
                new ClockDisplayConnection(
                    MinutesToHours12RequirementId,
                    MinutesNodeId,
                    Hours12NodeId,
                    RotationDirection.Same,
                    Rational.Zero),
                new ClockDisplayConnection(
                    MinutesToDay24RequirementId,
                    MinutesNodeId,
                    Day24NodeId,
                    RotationDirection.Same,
                    Rational.Zero),
            });
    }
}

public sealed class ClockCompilationOptions
{
    public ClockCompilationOptions(SemanticCompilerFingerprint compilerFingerprint)
    {
        CompilerFingerprint = compilerFingerprint ?? throw new ArgumentNullException(nameof(compilerFingerprint));
    }

    public SemanticCompilerFingerprint CompilerFingerprint { get; }

    public static ClockCompilationOptions Default { get; } = new(new SemanticCompilerFingerprint(
        ClockSemanticContract.CompilerId,
        ClockSemanticContract.CompilerVersion,
        ClockSemanticContract.DefaultDeterminismProfile));
}

public sealed class ClockRecipeCompiler
{
    public SemanticPeriodicSpecification Expand(ClockDisplayRecipe recipe)
    {
        if (recipe is null) throw new ArgumentNullException(nameof(recipe));
        return new SemanticPeriodicSpecification(
            ClockSemanticContract.SourceKind,
            PeriodicSemanticContract.BaseTimeUnitSeconds,
            recipe.Displays.Select(display => new PeriodicRotationNode(
                display.SemanticNodeId,
                display.ExactPeriodSeconds,
                display.ExactInitialPhaseTurns)),
            recipe.Connections.Select(connection => (SemanticMechanicalRequirement)new ContinuousRotationRequirement(
                connection.Id,
                connection.FromSemanticNodeId,
                connection.ToSemanticNodeId,
                connection.RequiredDirection,
                connection.ExactPhaseRelation,
                connection.OptionalExplicitTransfer)));
    }

    public SemanticCompilationResult Compile(
        ClockDisplayRecipe recipe,
        ClockCompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (recipe is null) throw new ArgumentNullException(nameof(recipe));
        var selectedOptions = options ?? ClockCompilationOptions.Default;
        var specification = Expand(recipe);
        var semanticId = ComputeSemanticSpecificationId(recipe);
        var compiler = new PeriodicSemanticCompiler(selectedOptions.CompilerFingerprint);
        return compiler.Compile(specification, semanticId, recipe.CatalogId, cancellationToken);
    }

    public static string ComputeSemanticSpecificationId(ClockDisplayRecipe recipe)
    {
        if (recipe is null) throw new ArgumentNullException(nameof(recipe));
        var builder = new StringBuilder("clock-display-recipe-v1|catalog=");
        builder.Append(PeriodicSemanticIdentity.LengthPrefixed(recipe.CatalogId));
        foreach (var display in recipe.Displays.OrderBy(item => item.SemanticNodeId, StringComparer.Ordinal))
        {
            builder.Append("|display=").Append(PeriodicSemanticIdentity.LengthPrefixed(
                display.Role.ToString() + "|node=" + display.SemanticNodeId +
                "|periodSeconds=" + display.ExactPeriodSeconds +
                "|phaseTurns=" + display.ExactInitialPhaseTurns));
        }

        foreach (var connection in recipe.Connections.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.Append("|connection=").Append(PeriodicSemanticIdentity.LengthPrefixed(
                connection.Id + "|from=" + connection.FromSemanticNodeId +
                "|to=" + connection.ToSemanticNodeId +
                "|direction=" + connection.RequiredDirection +
                "|phase=" + connection.ExactPhaseRelation +
                "|explicit=" + (connection.OptionalExplicitTransfer?.ToString() ?? "none")));
        }

        return PeriodicSemanticIdentity.Hash("semantic-specification-sha256:", builder.ToString());
    }
}
