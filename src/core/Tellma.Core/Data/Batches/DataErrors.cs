// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    /// <summary>How the executor treats a failed round trip.</summary>
    internal enum FailureKind
    {
        /// <summary>The server reported an error that re-running cannot fix: it surfaces, translated.</summary>
        Final,

        /// <summary>
        ///     The server reported a transient error, and under <c>XACT_ABORT</c> rolled the batch back:
        ///     re-running the whole round trip is safe whatever its statements do.
        /// </summary>
        Reported,

        /// <summary>The connection was lost after the command was sent: whether the batch committed is unknown.</summary>
        Ambiguous,

        /// <summary>
        ///     The client failed while reading, on an open connection: a defect of a statement's
        ///     reader, which would fail again, while the server may have run the batch to its commit.
        /// </summary>
        Defect,
    }

    /// <summary>The fields of one server error the executor classifies and translates by.</summary>
    /// <param name="Number">The error number; negative for a client-side condition such as a timeout.</param>
    /// <param name="Class">The severity; 20 and above end the connection.</param>
    /// <param name="Message">The message: the stable code of a platform or distribution <c>THROW</c>.</param>
    /// <param name="Procedure">The trigger or procedure the error was raised in; empty for the batch text itself.</param>
    /// <param name="LineNumber">The line the error was raised on, in the batch text or in <paramref name="Procedure" />.</param>
    internal readonly record struct SqlErrorInfo(int Number, byte Class, string Message, string? Procedure, int LineNumber)
    {
        /// <summary>The informational "statement has been terminated" that follows many errors, never the cause.</summary>
        private const int StatementTerminated = 3621;

        /// <summary>The fields of every error an exception carries, in the order the server raised them.</summary>
        /// <param name="exception">The provider's exception.</param>
        /// <returns>The errors; one built from the exception itself when it carries none.</returns>
        public static IReadOnlyList<SqlErrorInfo> Of(SqlException exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            List<SqlErrorInfo> errors = [.. exception.Errors.Cast<SqlError>().Select(static error => new SqlErrorInfo(
                error.Number, error.Class, error.Message, error.Procedure, error.LineNumber))];
            return errors.Count > 0
                ? errors
                : [new SqlErrorInfo(exception.Number, exception.Class, exception.Message, exception.Procedure, exception.LineNumber)];
        }

        /// <summary>
        ///     The error that caused a failure: the first error above informational severity other than
        ///     "the statement has been terminated", else the first error.
        /// </summary>
        /// <param name="errors">The errors, in order; at least one.</param>
        /// <returns>The primary error.</returns>
        public static SqlErrorInfo Primary(IReadOnlyList<SqlErrorInfo> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            ArgumentOutOfRangeException.ThrowIfZero(errors.Count);
            foreach (SqlErrorInfo error in errors)
            {
                if (error.Class > 10 && error.Number != StatementTerminated)
                {
                    return error;
                }
            }

            return errors[0];
        }
    }

    // The classification and translation tables are spec 0012 §6.3 and §6.4.
    /// <summary>
    ///     Classifies a failed round trip's server errors for the retry rules, and translates a final
    ///     error into the data layer's exception for it.
    /// </summary>
    internal static partial class DataErrors
    {
        /// <summary>The client's command timeout: the server may still have run the batch.</summary>
        public const int CommandTimeout = -2;

        /// <summary>The numbers the server reports for a failure that re-running the round trip may cure.</summary>
        public static readonly FrozenSet<int> ReportedTransientNumbers =
            new[] { 1205, 1222, 4060, 10928, 10929, 40197, 40501, 40613, 49918, 49919, 49920, TellmaSqlErrors.Transient }.ToFrozenSet();

        /// <summary>The numbers of a lost connection: a broken link, a dropped session, a timeout.</summary>
        public static readonly FrozenSet<int> AmbiguousNumbers = new[] { 233, 64, CommandTimeout, 10053, 10054 }.ToFrozenSet();

        /// <summary>The severity from which an error ends the connection.</summary>
        private const byte FatalClass = 20;

        /// <summary>A primary key violation.</summary>
        private const int PrimaryKeyViolation = 2627;

        /// <summary>A unique index violation.</summary>
        private const int UniqueIndexViolation = 2601;

        /// <summary>A foreign key or check constraint violation.</summary>
        private const int ConstraintViolation = 547;

        /// <summary>A recursive query exhausted its <c>MAXRECURSION</c>.</summary>
        private const int RecursionExhausted = 530;

        /// <summary>A division by zero.</summary>
        private const int DivideByZero = 8134;

        /// <summary>An arithmetic overflow.</summary>
        private const int ArithmeticOverflow = 8115;

        /// <summary>The lowest number a <c>THROW</c> may raise.</summary>
        private const int ThrowMin = 50000;

        /// <summary>The platform-band numbers with a meaning; any other number of the band is a defect.</summary>
        private static readonly FrozenSet<int> PlatformNumbers = new[]
        {
            TellmaSqlErrors.CallerInvalid, TellmaSqlErrors.RowSecurity, TellmaSqlErrors.NotFound, TellmaSqlErrors.Concurrency,
            TellmaSqlErrors.StaleVersionTag, TellmaSqlErrors.LimitExceeded, TellmaSqlErrors.Invariant, TellmaSqlErrors.CountMismatch,
            TellmaSqlErrors.SchemaBehind, TellmaSqlErrors.Transient,
        }.ToFrozenSet();

        /// <summary>Classifies a failure the server reported after the command was sent.</summary>
        /// <param name="errors">The errors, in order; at least one.</param>
        /// <returns>
        ///     <see cref="FailureKind.Reported" /> when the primary error is transient, at any severity;
        ///     <see cref="FailureKind.Ambiguous" /> when any error is a lost connection;
        ///     <see cref="FailureKind.Final" /> otherwise.
        /// </returns>
        public static FailureKind Classify(IReadOnlyList<SqlErrorInfo> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);

            // A transient number is the server's own account of the failure, even when it ends the
            // session: Azure SQL raises throttling (40501, 10928, 10929) at severity 20, and the
            // server rolled the batch back, so it is re-run as reported rather than probed.
            if (ReportedTransientNumbers.Contains(SqlErrorInfo.Primary(errors).Number))
            {
                return FailureKind.Reported;
            }

            // A connection-level error anywhere in the list means the session is gone, whatever else the
            // server said before it went.
            foreach (SqlErrorInfo error in errors)
            {
                if (AmbiguousNumbers.Contains(error.Number) || error.Class >= FatalClass)
                {
                    return FailureKind.Ambiguous;
                }
            }

            return FailureKind.Final;
        }

        /// <summary>
        ///     The batch statement an error came from: the statement whose lines hold the error's line,
        ///     unless the error was raised inside a trigger or procedure, whose line numbers are its own.
        /// </summary>
        /// <param name="error">The error.</param>
        /// <param name="assembly">The batch's assembly.</param>
        /// <returns>The statement's ordinal, or null.</returns>
        public static int? StatementOrdinal(SqlErrorInfo error, BatchAssembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            return string.IsNullOrEmpty(error.Procedure) ? assembly.StatementOrdinalAt(error.LineNumber) : null;
        }

        /// <summary>Translates a final server error into the exception the data layer surfaces for it.</summary>
        /// <param name="errors">The errors, in order; at least one.</param>
        /// <param name="exception">The provider's exception, which the translation carries as its inner exception.</param>
        /// <param name="assembly">The batch's assembly, which resolves the error's line to a statement.</param>
        /// <param name="cursor">The round trip's cursor, holding the rows a statement reported before it threw; null before the send.</param>
        /// <returns>The exception to throw.</returns>
        public static Exception Translate(IReadOnlyList<SqlErrorInfo> errors, Exception exception, BatchAssembly assembly, ResultSetCursor? cursor)
        {
            ArgumentNullException.ThrowIfNull(exception);
            var primary = SqlErrorInfo.Primary(errors);
            int? ordinal = StatementOrdinal(primary, assembly);
            Type? entityType = ordinal is int known ? assembly.Statements.FirstOrDefault(statement => statement.Ordinal == known)?.EntityType : null;
            string code = primary.Message;
            int number = primary.Number;

            // --- The platform band: each number its own exception; an unknown one is a defect ---
            if (TellmaSqlErrors.IsPlatform(number))
            {
                return number switch
                {
                    TellmaSqlErrors.RowSecurity => new RowSecurityException(ordinal, exception),
                    TellmaSqlErrors.Concurrency => cursor?.Conflicts is IReadOnlyList<ConflictRow> conflicts
                        ? new ConcurrencyConflictException(conflicts, ordinal, exception)
                        : Unreported(number, "conflict rows", ordinal, exception),
                    TellmaSqlErrors.NotFound => cursor?.MissingIds is IReadOnlyList<long> missing
                        ? new BatchAssertionFailedException(number, code, ordinal, exception, missing)
                        : Unreported(number, "missing ids", ordinal, exception),
                    TellmaSqlErrors.Invariant when code == TellmaSqlErrors.TreeCycleCode => new TreeCycleException(entityType, ordinal, exception),
                    TellmaSqlErrors.Invariant when code == TellmaSqlErrors.VersionTagMissingCode => new InvalidOperationException(
                        "A version-tag bump found no row for a tag the batch bumps, so the tenant database lacks a registered tag. Run the migrator, which seeds every registered tag.",
                        exception),
                    _ when PlatformNumbers.Contains(number) => new BatchAssertionFailedException(number, code, ordinal, exception),
                    _ => OutOfBand(number, ordinal, exception),
                };
            }

            // --- The distribution band: always an assertion with its code ---
            if (TellmaSqlErrors.IsDistribution(number))
            {
                return new BatchAssertionFailedException(number, code, ordinal, exception);
            }

            // --- The provider's own errors ---
            return number switch
            {
                PrimaryKeyViolation or UniqueIndexViolation => IndexName(number, code) is string index
                    ? new UniqueConstraintViolationException(index, ordinal, exception)
                    : new DataAccessException(primary.Message, ordinal, exception),
                ConstraintViolation => ForeignKeyName(code) is string constraint
                    ? new ForeignKeyViolationException(constraint, ordinal, exception)
                    : new DataAccessException(primary.Message, ordinal, exception),
                RecursionExhausted => new TreeDepthExceededException(entityType, ordinal, exception),
                DivideByZero => new ArithmeticFailureException(ArithmeticFailureException.DivideByZero, ordinal, exception),
                ArithmeticOverflow => new ArithmeticFailureException(ArithmeticFailureException.Overflow, ordinal, exception),
                >= ThrowMin => OutOfBand(number, ordinal, exception),
                _ => new DataAccessException(primary.Message, ordinal, exception),
            };
        }

        /// <summary>
        ///     The index or primary key a duplicate-key error names. The English messages name it after
        ///     <c>constraint</c> (2627) or <c>index</c> (2601); in another server language the name is
        ///     the first quoted name without a schema dot, the other quoted name being the table.
        /// </summary>
        /// <param name="number">2627 or 2601.</param>
        /// <param name="message">The error's message.</param>
        /// <returns>The name, or null when the message names none.</returns>
        internal static string? IndexName(int number, string message)
        {
            Match named = (number == PrimaryKeyViolation ? ConstraintPattern() : IndexPattern()).Match(message);
            return named.Success
                ? named.Groups["name"].Value
                : SingleQuotedPattern().Matches(message).Select(static match => match.Groups["name"].Value).FirstOrDefault(static name => !name.Contains('.', StringComparison.Ordinal));
        }

        /// <summary>
        ///     The foreign key a 547 error names, or null when it names a check constraint (a defect,
        ///     not a reference) or none. The constraint is the first double-quoted name.
        /// </summary>
        /// <param name="message">The error's message.</param>
        /// <returns>The name, or null.</returns>
        internal static string? ForeignKeyName(string message)
        {
            Match quoted = DoubleQuotedPattern().Match(message);
            if (!quoted.Success)
            {
                return null;
            }

            string name = quoted.Groups["name"].Value;
            return name.StartsWith("CK_", StringComparison.Ordinal) || message.Contains(" CHECK constraint ", StringComparison.Ordinal) ? null : name;
        }

        /// <summary>The refusal of a number outside the two bands, or of an unassigned platform number: a defect.</summary>
        /// <param name="number">The number.</param>
        /// <param name="ordinal">The statement, if known.</param>
        /// <param name="exception">The provider's exception.</param>
        /// <returns>The exception.</returns>
        private static InvalidOperationException OutOfBand(int number, int? ordinal, Exception exception)
        {
            return new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Describe(ordinal)} threw {number}, which is not a number platform or distribution SQL may throw. Platform SQL throws the assigned numbers of {TellmaSqlErrors.PlatformMin}–{TellmaSqlErrors.PlatformMax}; distribution SQL throws {TellmaSqlErrors.DistroMin}–{TellmaSqlErrors.DistroMax}."),
                exception);
        }

        /// <summary>The refusal of a number whose preceding result set no statement reported: a defect of the statement's reader.</summary>
        /// <param name="number">The number.</param>
        /// <param name="what">What the set holds.</param>
        /// <param name="ordinal">The statement, if known.</param>
        /// <param name="exception">The provider's exception.</param>
        /// <returns>The exception.</returns>
        private static InvalidOperationException Unreported(int number, string what, int? ordinal, Exception exception)
        {
            return new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Describe(ordinal)} threw {number}, but its reader reported no {what} from the result set before the throw. A statement that throws {number} reads that set and reports it to the cursor."),
                exception);
        }

        /// <summary>Names a statement for a message.</summary>
        /// <param name="ordinal">The statement, if known.</param>
        /// <returns>The name.</returns>
        private static string Describe(int? ordinal)
        {
            return ordinal is int known ? string.Create(CultureInfo.InvariantCulture, $"Batch statement {known}") : "The batch";
        }

        /// <summary>The name after <c>constraint</c> in the English 2627 message.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex("constraint '(?<name>[^']+)'", RegexOptions.CultureInvariant)]
        private static partial Regex ConstraintPattern();

        /// <summary>The name after <c>index</c> in the English 2601 message.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex("index '(?<name>[^']+)'", RegexOptions.CultureInvariant)]
        private static partial Regex IndexPattern();

        /// <summary>A single-quoted name.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex("'(?<name>[^']+)'", RegexOptions.CultureInvariant)]
        private static partial Regex SingleQuotedPattern();

        /// <summary>A double-quoted name.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex("\"(?<name>[^\"]+)\"", RegexOptions.CultureInvariant)]
        private static partial Regex DoubleQuotedPattern();
    }
}
