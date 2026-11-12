// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data
{
    // The exceptions one statement of a batch raises (spec 0012 §6.3, §6.4). Each carries the
    // ordinal of that statement, so the pipeline attributes a persist-time error to the group of
    // statements that holds it.

    /// <summary>A statement inserted or updated a row that duplicates another under a unique index or the primary key.</summary>
    /// <param name="indexName">The index or primary key, as the server names it: <c>UX_Widgets_Code</c>, <c>PK_Widgets</c>.</param>
    /// <param name="statementOrdinal">The ordinal of the statement that raised it, or null.</param>
    /// <param name="innerException">The provider's exception.</param>
    public sealed class UniqueConstraintViolationException(string indexName, int? statementOrdinal, Exception? innerException = null)
        : DataAccessException($"{Describe(statementOrdinal)} violated the unique index '{indexName}'.", statementOrdinal, innerException)
    {
        /// <summary>
        ///     The index or primary key the row duplicates. The pipeline maps it to a property through
        ///     the entity's unique indexes, never through the provider's message.
        /// </summary>
        public string IndexName { get; } = indexName;
    }

    /// <summary>A statement wrote a reference to a row that does not exist, or deleted a row that is still referenced.</summary>
    /// <param name="constraintName">The foreign key, as the server names it: <c>FK_Widgets_ImageId</c>.</param>
    /// <param name="statementOrdinal">The ordinal of the statement that raised it, or null.</param>
    /// <param name="innerException">The provider's exception.</param>
    public sealed class ForeignKeyViolationException(string constraintName, int? statementOrdinal, Exception? innerException = null)
        : DataAccessException($"{Describe(statementOrdinal)} violated the foreign key '{constraintName}'.", statementOrdinal, innerException)
    {
        /// <summary>The foreign key, from which the pipeline derives the table and column.</summary>
        public string ConstraintName { get; } = constraintName;
    }

    /// <summary>A tree statement found a parent chain that loops back on itself.</summary>
    /// <param name="entityType">The tree's entity type, when the statement declared it.</param>
    /// <param name="statementOrdinal">The ordinal of the statement that raised it, or null.</param>
    /// <param name="innerException">The provider's exception.</param>
    public sealed class TreeCycleException(Type? entityType, int? statementOrdinal, Exception? innerException = null)
        : DataAccessException($"{Describe(statementOrdinal)} found a cycle in the tree of {entityType?.Name ?? "an entity"}.", statementOrdinal, innerException)
    {
        /// <summary>The tree's entity type, when the statement declared it.</summary>
        public Type? EntityType { get; } = entityType;
    }

    /// <summary>A tree statement found a parent chain deeper than the tree's maximum depth.</summary>
    /// <param name="entityType">The tree's entity type, when the statement declared it.</param>
    /// <param name="statementOrdinal">The ordinal of the statement that raised it, or null.</param>
    /// <param name="innerException">The provider's exception.</param>
    public sealed class TreeDepthExceededException(Type? entityType, int? statementOrdinal, Exception? innerException = null)
        : DataAccessException($"{Describe(statementOrdinal)} found a chain deeper than the tree of {entityType?.Name ?? "an entity"} allows.", statementOrdinal, innerException)
    {
        /// <summary>The tree's entity type, when the statement declared it.</summary>
        public Type? EntityType { get; } = entityType;
    }

    /// <summary>A statement addressed a row outside the caller's grant.</summary>
    /// <param name="statementOrdinal">The ordinal of the statement that raised it, or null.</param>
    /// <param name="innerException">The provider's exception.</param>
    public sealed class RowSecurityException(int? statementOrdinal, Exception? innerException = null)
        : DataAccessException($"{Describe(statementOrdinal)} addressed a row outside the caller's grant.", statementOrdinal, innerException);

    /// <summary>
    ///     A statement's concurrency guard found rows whose stamp differs from the one the caller
    ///     supplied, or that no longer exist.
    /// </summary>
    /// <param name="conflicts">The guard's conflict rows, as its result set returned them.</param>
    /// <param name="statementOrdinal">The ordinal of the statement that raised it, or null.</param>
    /// <param name="innerException">The provider's exception.</param>
    public sealed class ConcurrencyConflictException(IReadOnlyList<ConflictRow> conflicts, int? statementOrdinal, Exception? innerException = null)
        : DataAccessException(Describe(conflicts, statementOrdinal), statementOrdinal, innerException)
    {
        /// <summary>The guard's conflict rows, as its result set returned them.</summary>
        public IReadOnlyList<ConflictRow> Conflicts { get; } = [.. conflicts];

        /// <summary>The message: how many rows conflict and how many are missing.</summary>
        /// <param name="conflicts">The rows.</param>
        /// <param name="statementOrdinal">The statement.</param>
        /// <returns>The message.</returns>
        private static string Describe(IReadOnlyList<ConflictRow> conflicts, int? statementOrdinal)
        {
            ArgumentNullException.ThrowIfNull(conflicts);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Describe(statementOrdinal)} found {conflicts.Count} conflicting rows, {conflicts.Count(static row => row.IsMissing)} of them missing.");
        }
    }

    /// <summary>One row a concurrency guard reports: its id, whether it is gone, and who last changed it.</summary>
    /// <param name="Id">The row's id.</param>
    /// <param name="IsMissing">True when the row no longer exists.</param>
    /// <param name="ModifiedAt">The row's stored stamp; null when it is missing.</param>
    /// <param name="ModifiedById">The user who last changed the row; null when it is missing.</param>
    /// <param name="Name">That user's name in the primary language.</param>
    /// <param name="Name2">That user's name in the secondary language.</param>
    /// <param name="Name3">That user's name in the ternary language.</param>
    public sealed record ConflictRow(
        long Id, bool IsMissing, DateTimeOffset? ModifiedAt, int? ModifiedById, string? Name, string? Name2, string? Name3);

    /// <summary>
    ///     A statement's <c>THROW</c> of a number in the platform or distribution band that maps to no
    ///     finer exception: the number and the code its message carries.
    /// </summary>
    public sealed class BatchAssertionFailedException : DataAccessException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="number">The thrown number.</param>
        /// <param name="code">The thrown message: a stable code such as <c>Delete.CapExceeded</c>.</param>
        /// <param name="statementOrdinal">The ordinal of the statement that raised it, or null for the batch's frame.</param>
        /// <param name="innerException">The provider's exception.</param>
        /// <param name="missingIds">The ids a not-found check reported before it threw; none when null.</param>
        public BatchAssertionFailedException(int number, string code, int? statementOrdinal, Exception? innerException = null, IReadOnlyList<long>? missingIds = null)
            : base(string.Create(CultureInfo.InvariantCulture, $"{Describe(statementOrdinal)} failed the assertion {number} '{code}'."), statementOrdinal, innerException)
        {
            ArgumentNullException.ThrowIfNull(code);
            Number = number;
            Code = code;
            MissingIds = missingIds is null ? [] : [.. missingIds];
        }

        /// <summary>The thrown number.</summary>
        public int Number { get; }

        /// <summary>The thrown message: a stable code.</summary>
        public string Code { get; }

        /// <summary>
        ///     The ids a <see cref="TellmaSqlErrors.NotFound" /> check returned in the result set before
        ///     its <c>THROW</c>; empty for every other number.
        /// </summary>
        public IReadOnlyList<long> MissingIds { get; }
    }

    /// <summary>A statement divided by zero or overflowed a numeric type.</summary>
    /// <remarks>Creating it with a reason other than the two throws <see cref="ArgumentException" />.</remarks>
    /// <param name="reason"><see cref="DivideByZero" /> or <see cref="Overflow" />.</param>
    /// <param name="statementOrdinal">The ordinal of the statement that raised it, or null.</param>
    /// <param name="innerException">The provider's exception.</param>
    public sealed class ArithmeticFailureException(string reason, int? statementOrdinal, Exception? innerException = null)
        : DataAccessException($"{Describe(statementOrdinal)} failed an arithmetic operation: {ValidateReason(reason)}.", statementOrdinal, innerException)
    {
        /// <summary>The reason of a division by zero.</summary>
        public const string DivideByZero = "divide-by-zero";

        /// <summary>The reason of an arithmetic overflow.</summary>
        public const string Overflow = "overflow";

        /// <summary><see cref="DivideByZero" /> or <see cref="Overflow" />.</summary>
        public string Reason { get; } = reason;

        /// <summary>Refuses a reason outside the two.</summary>
        /// <param name="reason">The reason.</param>
        /// <returns>The reason.</returns>
        private static string ValidateReason(string reason)
        {
            return reason is DivideByZero or Overflow
                ? reason
                : throw new ArgumentException($"'{reason}' is not an arithmetic failure reason.", nameof(reason));
        }
    }
}
