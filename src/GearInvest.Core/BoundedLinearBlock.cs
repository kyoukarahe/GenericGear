using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace GearInvest.Core;

/// <summary>A bounded exact row; RHS columns represent constant and explicitly selected input symbols.</summary>
public sealed class ExactLinearRow
{
    public ExactLinearRow(string id, IEnumerable<Rational> coefficients, IEnumerable<Rational> rightHandSide)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 320) throw new ArgumentException("Linear row ID required.");
        Id = id; Coefficients = Copy(coefficients, 4); RightHandSide = Copy(rightHandSide, 3);
    }
    private static ReadOnlyCollection<Rational> Copy(IEnumerable<Rational> values, int max)
    {
        var a = (values ?? throw new ArgumentNullException(nameof(values))).Take(max + 1).ToArray();
        if (a.Length == 0 || a.Length > max) throw new ArgumentException("Linear row dimension bound exceeded.");
        foreach (var v in a) BoundedLinearBlock.Bound(v);
        return Array.AsReadOnly(a);
    }
    public string Id { get; }
    public ReadOnlyCollection<Rational> Coefficients { get; }
    public ReadOnlyCollection<Rational> RightHandSide { get; }
}

public sealed class ExactReducedRow
{
    internal ExactReducedRow(int pivot, Rational[] a, Rational[] b, IEnumerable<string> witnesses)
    { Pivot = pivot; Coefficients = Array.AsReadOnly((Rational[])a.Clone()); RightHandSide = Array.AsReadOnly((Rational[])b.Clone()); WitnessRows = witnesses.OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly(); }
    public int Pivot { get; }
    public ReadOnlyCollection<Rational> Coefficients { get; }
    public ReadOnlyCollection<Rational> RightHandSide { get; }
    /// <summary>Elimination lineage, not a minimal unsatisfiable core.</summary>
    public ReadOnlyCollection<string> WitnessRows { get; }
}

public sealed class ExactLinearReduction
{
    internal ExactLinearReduction(IEnumerable<ExactReducedRow> rows, int columns, int operations)
    {
        Rows = rows.ToList().AsReadOnly(); Rank = Rows.Count(r => r.Pivot >= 0);
        FreeColumns = Enumerable.Range(0, columns).Where(c => !Rows.Any(r => r.Pivot == c)).ToList().AsReadOnly();
        HasIncompatibleRightHandSide = Rows.Any(r => r.Pivot < 0 && r.RightHandSide.Any(v => v != 0)); Operations = operations;
    }
    public ReadOnlyCollection<ExactReducedRow> Rows { get; }
    public ReadOnlyCollection<int> FreeColumns { get; }
    public int Rank { get; }
    public int Operations { get; }
    public bool HasIncompatibleRightHandSide { get; }
}

public sealed class ExactLinearResourceException : ArgumentException
{
    public ExactLinearResourceException(string message) : base(message) { }
}

/// <summary>Small exact elimination primitive, not mechanical admission or an unrestricted symbolic solver.</summary>
public static class BoundedLinearBlock
{
    public const int MaxOperations = 50000;
    public const int MaxDerivedDigits = 1024;
    internal static void Bound(Rational r)
    {
        if (r.Numerator.ToString(CultureInfo.InvariantCulture).Length > MaxDerivedDigits || r.Denominator.ToString(CultureInfo.InvariantCulture).Length > MaxDerivedDigits)
            throw new ExactLinearResourceException("Exact linear derived fraction limit exceeded.");
    }
    public static ExactLinearReduction Reduce(int columns, int rightHandSideColumns, IEnumerable<ExactLinearRow> rows)
    {
        if (columns < 1 || columns > 4 || rightHandSideColumns < 1 || rightHandSideColumns > 3) throw new ArgumentException("Exact linear dimension bound exceeded.");
        var input = (rows ?? throw new ArgumentNullException(nameof(rows))).Take(17).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        if (input.Length > 16 || input.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != input.Length || input.Any(r => r.Coefficients.Count != columns || r.RightHandSide.Count != rightHandSideColumns))
            throw new ArgumentException("Exact linear row bound, identity or dimensions invalid.");
        var a = input.Select(r => r.Coefficients.ToArray()).ToArray(); var b = input.Select(r => r.RightHandSide.ToArray()).ToArray();
        var lineage = input.Select(r => new SortedSet<string>(new[] { r.Id }, StringComparer.Ordinal)).ToArray();
        var pivots = Enumerable.Repeat(-1, input.Length).ToArray(); int next = 0, operations = 0;
        Rational Checked(Rational v) { if (++operations > MaxOperations) throw new ExactLinearResourceException("Exact linear work limit exceeded."); Bound(v); return v; }
        for (int col = 0; col < columns && next < input.Length; col++)
        {
            int row = next; while (row < input.Length && a[row][col] == 0) row++;
            if (row == input.Length) continue;
            (a[next], a[row]) = (a[row], a[next]); (b[next], b[row]) = (b[row], b[next]); (lineage[next], lineage[row]) = (lineage[row], lineage[next]);
            var divisor = a[next][col];
            for (int c = 0; c < columns; c++) a[next][c] = Checked(a[next][c] / divisor);
            for (int c = 0; c < rightHandSideColumns; c++) b[next][c] = Checked(b[next][c] / divisor);
            for (int r = 0; r < input.Length; r++)
            {
                if (r == next || a[r][col] == 0) continue;
                var multiplier = a[r][col]; lineage[r].UnionWith(lineage[next]);
                for (int c = 0; c < columns; c++) a[r][c] = Checked(a[r][c] - Checked(multiplier * a[next][c]));
                for (int c = 0; c < rightHandSideColumns; c++) b[r][c] = Checked(b[r][c] - Checked(multiplier * b[next][c]));
            }
            pivots[next++] = col;
        }
        return new(Enumerable.Range(0, input.Length).Select(r => new ExactReducedRow(pivots[r], a[r], b[r], lineage[r])), columns, operations);
    }
}
