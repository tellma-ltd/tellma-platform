// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Globalization;
using System.Text;

namespace Tellma.Core.Analyzers.Sql
{
    /// <summary>What a raw statement's hole binds as, read from its static type.</summary>
    internal enum SqlHoleKind
    {
        /// <summary>A scalar value, bound as a parameter.</summary>
        Value,

        /// <summary>A <c>SqlIdentifier</c>, emitted as text.</summary>
        Identifier,

        /// <summary>A list of table-type rows, bound as a table-valued parameter.</summary>
        Table,

        /// <summary>A bare <c>nameof</c>, which binds a string parameter where a name was meant.</summary>
        NameOf,

        /// <summary>A hole with an alignment or a format string, which the batch refuses.</summary>
        Formatted,

        /// <summary>A value of a type the batch cannot bind.</summary>
        Unsupported,
    }

    /// <summary>What an identifier hole names, as far as the code shows.</summary>
    internal enum IdentifierNature
    {
        /// <summary>Not knowable from the hole's expression.</summary>
        Unknown,

        /// <summary>A table variable or table-valued parameter the batch declared, emitted as is.</summary>
        Variable,

        /// <summary>An object name the batch bracket-quotes, known as text.</summary>
        ObjectName,
    }

    /// <summary>
    ///     One hole of a raw statement: what it binds as, and the stand-in written in its place in
    ///     the text the parser reads.
    /// </summary>
    internal sealed class SqlHole
    {
        /// <summary>The prefix of every stand-in, which no statement of the platform's would name.</summary>
        public const string PlaceholderStem = "__tellma_hole";

        /// <summary>The scalar types the batch binds, by special type.</summary>
        private static readonly HashSet<SpecialType> ScalarSpecialTypes =
        [
            SpecialType.System_String,
            SpecialType.System_Char,
            SpecialType.System_Boolean,
            SpecialType.System_Byte,
            SpecialType.System_Int16,
            SpecialType.System_Int32,
            SpecialType.System_Int64,
            SpecialType.System_Decimal,
            SpecialType.System_Double,
            SpecialType.System_Single,
            SpecialType.System_DateTime,
            SpecialType.System_Enum,
        ];

        /// <summary>The scalar types the batch binds that have no special type, in the <c>System</c> namespace.</summary>
        private static readonly HashSet<string> ScalarSystemTypes = ["Guid", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan"];

        /// <summary>Classifies one interpolation.</summary>
        /// <param name="index">The hole's index in the statement.</param>
        /// <param name="interpolation">The interpolation.</param>
        public SqlHole(int index, IInterpolationOperation interpolation)
        {
            Index = index;
            Value = interpolation.Expression.WithoutImplicitConversions();
            Location = interpolation.Syntax.GetLocation();
            Constant = Value.ConstantValue;
            TypeName = Value.Type?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            // --- What the hole binds as ---
            if (interpolation.Alignment is not null || interpolation.FormatString is not null)
            {
                Kind = SqlHoleKind.Formatted;
            }
            else if (Value is INameOfOperation)
            {
                Kind = SqlHoleKind.NameOf;
            }
            else if (Value.Type is null)
            {
                // The null literal binds SQL NULL; anything else typeless is not a value.
                Kind = Value.ConstantValue is { HasValue: true, Value: null } ? SqlHoleKind.Value : SqlHoleKind.Unsupported;
            }
            else if (Value.Type.IsOrDerivesFrom(KnownNames.DataNamespace, KnownNames.SqlIdentifier))
            {
                Kind = SqlHoleKind.Identifier;
                (Nature, ObjectName) = Identify(Value, depth: 0);
            }
            else
            {
                Kind = IsScalar(Value.Type) ? SqlHoleKind.Value
                    : IsRowList(Value.Type) ? SqlHoleKind.Table
                    : SqlHoleKind.Unsupported;
            }

            // --- The stand-in the parser reads ---
            Placeholder = Kind == SqlHoleKind.Identifier && Nature == IdentifierNature.ObjectName ? Quote(ObjectName!)
                : Kind == SqlHoleKind.Identifier && Nature == IdentifierNature.Unknown ? $"[{PlaceholderStem}{index.ToString(CultureInfo.InvariantCulture)}]"
                : $"@{PlaceholderStem}{index.ToString(CultureInfo.InvariantCulture)}";
        }

        /// <summary>The hole's index in the statement.</summary>
        public int Index { get; }

        /// <summary>The hole's expression, without the conversion to <c>object</c>.</summary>
        public IOperation Value { get; }

        /// <summary>Where the hole is.</summary>
        public Location Location { get; }

        /// <summary>The hole's compile-time constant, if it has one.</summary>
        public Optional<object?> Constant { get; }

        /// <summary>The hole's static type, for messages; null for the null literal.</summary>
        public string? TypeName { get; }

        /// <summary>What the hole binds as.</summary>
        public SqlHoleKind Kind { get; }

        /// <summary>What an identifier hole names; <see cref="IdentifierNature.Unknown" /> for every other kind.</summary>
        public IdentifierNature Nature { get; }

        /// <summary>The object name an identifier hole names, when known.</summary>
        public string? ObjectName { get; }

        /// <summary>The text written in the hole's place.</summary>
        public string Placeholder { get; }

        /// <summary>Whether the hole stands for a batch table: a table-valued parameter, or a variable the batch declared.</summary>
        public bool IsBatchTable => Kind == SqlHoleKind.Table || (Kind == SqlHoleKind.Identifier && Nature == IdentifierNature.Variable);

        /// <summary>Reads the hole index out of a stand-in's name, or -1 when the name is no stand-in.</summary>
        /// <param name="name">The name, with or without its <c>@</c> or brackets.</param>
        /// <returns>The index.</returns>
        public static int IndexOf(string name)
        {
            int start = name.IndexOf(PlaceholderStem, StringComparison.Ordinal);
            if (start < 0)
            {
                return -1;
            }

            int index = 0;
            int position = start + PlaceholderStem.Length;
            int digits = 0;
            while (position < name.Length && name[position] is >= '0' and <= '9')
            {
                index = (index * 10) + (name[position] - '0');
                position++;
                digits++;
            }

            return digits > 0 ? index : -1;
        }

        /// <summary>Bracket-quotes an object name part by part, as the batch does.</summary>
        /// <param name="name">The dotted name.</param>
        /// <returns>The quoted name.</returns>
        private static string Quote(string name)
        {
            StringBuilder quoted = new(name.Length + 8);
            string[] parts = name.Split('.');
            for (int index = 0; index < parts.Length; index++)
            {
                if (index > 0)
                {
                    _ = quoted.Append('.');
                }

                _ = quoted.Append('[').Append(parts[index].Replace("]", "]]")).Append(']');
            }

            return quoted.ToString();
        }

        /// <summary>Whether a type binds as a scalar parameter, nullable forms included.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True when it does.</returns>
        private static bool IsScalar(ITypeSymbol type)
        {
            ITypeSymbol underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                ? nullable.TypeArguments[0]
                : type;

            return ScalarSpecialTypes.Contains(underlying.SpecialType)
                || underlying.TypeKind == TypeKind.Enum
                || underlying is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte }
                || (underlying is INamedTypeSymbol { ContainingType: null } named
                    && ScalarSystemTypes.Contains(named.MetadataName)
                    && named.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true });
        }

        /// <summary>Whether a type is a read-only list of reference-type rows, which the batch binds as a table-valued parameter.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True when it is.</returns>
        private static bool IsRowList(ITypeSymbol type)
        {
            return type.FindInterfaces("System.Collections.Generic", "IReadOnlyList`1")
                .Any(static list => list.TypeArguments[0].IsReferenceType);
        }

        /// <summary>Finds what an identifier expression names, following a local back to its initializer.</summary>
        /// <param name="value">The expression.</param>
        /// <param name="depth">How many locals were followed already.</param>
        /// <returns>The nature, and the object name when it is one.</returns>
        private static (IdentifierNature Nature, string? Name) Identify(IOperation value, int depth)
        {
            switch (value.WithoutConversions())
            {
                // --- A name the batch declared: Tvp, DeclareIdTable, a save's or a delete's capture ---
                case IInvocationOperation invocation
                    when invocation.TargetMethod.Name is "Tvp" or "DeclareIdTable"
                        && invocation.TargetMethod.IsOnTypeImplementing(KnownNames.DataNamespace, KnownNames.DataBatch):
                case IPropertyReferenceOperation property
                    when property.Property.ContainingType.Is(KnownNames.DataNamespace, KnownNames.SaveHandle)
                        || property.Property.ContainingType.Is(KnownNames.DataNamespace, KnownNames.DeleteHandle)
                        || property.Property.ContainingType.Is(KnownNames.DataNamespace, KnownNames.ColumnCapture):
                    return (IdentifierNature.Variable, null);

                // --- A name written in the code ---
                case IObjectCreationOperation creation
                    when creation.Arguments.Length > 0 && creation.Arguments[0].Value.ConstantValue is { HasValue: true, Value: string name }:
                    return name.StartsWith("@", StringComparison.Ordinal) ? (IdentifierNature.Variable, null) : (IdentifierNature.ObjectName, name);

                // --- Either branch, when both agree ---
                case IConditionalOperation { WhenFalse: not null } conditional:
                    (IdentifierNature Nature, string? Name) whenTrue = Identify(conditional.WhenTrue, depth);
                    (IdentifierNature Nature, string? Name) whenFalse = Identify(conditional.WhenFalse, depth);
                    return whenTrue.Nature == IdentifierNature.Variable && whenFalse.Nature == IdentifierNature.Variable
                        ? (IdentifierNature.Variable, null)
                        : (IdentifierNature.Unknown, null);

                // --- A local, through its initializer ---
                case ILocalReferenceOperation local when depth < 4 && Initializer(local) is IOperation initializer:
                    return Identify(initializer, depth + 1);

                default:
                    return (IdentifierNature.Unknown, null);
            }
        }

        /// <summary>The initializer of a local, when it is declared with one in the same tree.</summary>
        /// <param name="local">The reference.</param>
        /// <returns>The initializer's value, or null.</returns>
        private static IOperation? Initializer(ILocalReferenceOperation local)
        {
            if (local.SemanticModel is not SemanticModel model)
            {
                return null;
            }

            foreach (SyntaxReference reference in local.Local.DeclaringSyntaxReferences)
            {
                if (reference.SyntaxTree == model.SyntaxTree
                    && model.GetOperation(reference.GetSyntax()) is IVariableDeclaratorOperation { Initializer.Value: IOperation value })
                {
                    return value;
                }
            }

            return null;
        }
    }
}
