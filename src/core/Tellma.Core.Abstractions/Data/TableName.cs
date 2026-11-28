// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     A table of a tenant database, by schema and name: <c>core.Users</c>, <c>gl.Centers</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The names are compared ordinally, exactly as the model spells them. A statement over a
    ///         table its caller chooses takes the table from the entity's metadata and writes it through
    ///         <see cref="Delimited" />, never by concatenating the parts itself.
    ///     </para>
    ///     <para>
    ///         <see cref="Schema" /> and <see cref="Name" /> are redeclared get-only, so a <c>with</c>
    ///         expression cannot blank what the constructor validated.
    ///     </para>
    /// </remarks>
    /// <param name="Schema">The table's schema, such as <c>core</c>.</param>
    /// <param name="Name">The table's name, such as <c>Users</c>.</param>
    public sealed record TableName(string Schema, string Name)
    {
        /// <inheritdoc cref="TableName(string, string)" />
        public string Schema { get; } = ValidatePart(Schema, nameof(Schema));

        /// <inheritdoc cref="TableName(string, string)" />
        public string Name { get; } = ValidatePart(Name, nameof(Name));

        /// <summary>
        ///     The table as a T-SQL identifier: <c>[core].[Users]</c>, each part in brackets with any
        ///     closing bracket in it doubled.
        /// </summary>
        public string Delimited => $"{Delimit(Schema)}.{Delimit(Name)}";

        /// <summary>The table as <c>schema.Name</c>, for messages and logs.</summary>
        /// <returns>The undelimited two-part name.</returns>
        public override string ToString()
        {
            return $"{Schema}.{Name}";
        }

        /// <summary>One part of a two-part name in brackets, any closing bracket in it doubled.</summary>
        /// <param name="part">The part.</param>
        /// <returns>The delimited part.</returns>
        private static string Delimit(string part)
        {
            return $"[{part.Replace("]", "]]", StringComparison.Ordinal)}]";
        }

        /// <summary>Refuses a null, empty or whitespace part.</summary>
        /// <param name="value">The part.</param>
        /// <param name="parameterName">The part's parameter name.</param>
        /// <returns><paramref name="value" />.</returns>
        private static string ValidatePart(string value, string parameterName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
            return value;
        }
    }
}
