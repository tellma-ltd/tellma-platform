// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Data.Common;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Tests.Data.Batches
{
    /// <summary>
    ///     The error taxonomy: which server errors are transient, which lose the connection and which
    ///     are final, and the exception each final error becomes, with the statement it came from.
    /// </summary>
    public sealed class DataErrorsTests
    {
        /// <summary>The line of the statement at ordinal 0: the frame takes lines 1 to 4.</summary>
        private const int FirstLine = 5;

        /// <summary>The line of the statement at ordinal 1, which composes for <see cref="Node" />.</summary>
        private const int SecondLine = 6;

        /// <summary>The provider's exception the translations carry.</summary>
        private static readonly Exception Provider = new InvalidDataException("The provider's exception.");

        /// <summary>A two-statement assembly whose second statement composes for an entity.</summary>
        private static readonly BatchAssembly Assembly = BatchAssembly.Create(
            [
                new BatchStatement(0, BatchSection.Body, "SELECT 0 AS [Zero];", [], new HashSet<TableName>(), idempotent: false, reader: null),
                new BatchStatement(1, BatchSection.Body, "SELECT 1 AS [One];", [], new HashSet<TableName>(), idempotent: false, reader: null, typeof(Node)),
            ],
            new string('0', 64),
            guarded: false,
            inTextTransaction: false);

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Theory]
        [InlineData(1205, 16)]
        [InlineData(1222, 16)]
        [InlineData(4060, 16)]
        [InlineData(10928, 16)]
        [InlineData(10929, 16)]
        [InlineData(40197, 16)]
        [InlineData(40501, 16)]
        [InlineData(40613, 16)]
        [InlineData(49918, 16)]
        [InlineData(49919, 16)]
        [InlineData(49920, 16)]
        [InlineData(TellmaSqlErrors.Transient, 16)]
        [InlineData(10928, 20)]
        [InlineData(10929, 20)]
        [InlineData(40197, 20)]
        [InlineData(40501, 20)]
        public void A_transient_number_the_server_reports_is_reported_at_any_severity(int number, int severity)
        {
            Assert.Equal(FailureKind.Reported, DataErrors.Classify([Error(number, (byte)severity)]));

            // The session-ending note the server may add after it does not hide the transient cause.
            Assert.Equal(FailureKind.Reported, DataErrors.Classify([Error(number, (byte)severity), Error(0, @class: 20)]));
        }

        [Theory]
        [InlineData(233)]
        [InlineData(64)]
        [InlineData(-2)]
        [InlineData(10053)]
        [InlineData(10054)]
        public void A_lost_connection_is_ambiguous_wherever_it_appears(int number)
        {
            Assert.Equal(FailureKind.Ambiguous, DataErrors.Classify([Error(number)]));
            Assert.Equal(FailureKind.Ambiguous, DataErrors.Classify([Error(2627), Error(number)]));
        }

        [Fact]
        public void A_fatal_severity_is_ambiguous_and_everything_else_is_final()
        {
            Assert.Equal(FailureKind.Ambiguous, DataErrors.Classify([Error(0, @class: 20)]));
            Assert.Equal(FailureKind.Final, DataErrors.Classify([Error(2627)]));
            Assert.Equal(FailureKind.Final, DataErrors.Classify([Error(TellmaSqlErrors.Concurrency)]));
            Assert.Equal(FailureKind.Final, DataErrors.Classify([Error(50000)]));

            // A transient number after the primary error does not make it transient.
            Assert.Equal(FailureKind.Final, DataErrors.Classify([Error(2627), Error(1205)]));
        }

        [Theory]
        [InlineData(50000, true)]
        [InlineData(TellmaSqlErrors.PlatformMin - 1, true)]
        [InlineData(TellmaSqlErrors.DistroMax + 1, true)]
        [InlineData(TellmaSqlErrors.PlatformMin, false)]
        [InlineData(TellmaSqlErrors.PlatformMax, false)]
        [InlineData(TellmaSqlErrors.DistroMin, false)]
        [InlineData(2627, false)]
        public void Only_a_user_defined_number_outside_both_bands_is_out_of_band(int number, bool outOfBand)
        {
            Assert.Equal(outOfBand, DataErrors.IsOutOfBand(number));
        }

        [Fact]
        public void The_primary_error_skips_informational_messages_and_the_statement_terminated_note()
        {
            SqlErrorInfo violation = Error(2627);
            Assert.Equal(violation, SqlErrorInfo.Primary([Error(0, @class: 0), violation, Error(3621, @class: 0)]));
            Assert.Equal(violation, SqlErrorInfo.Primary([Error(3621, @class: 14), violation]));

            // With no error above the informational level, the first is primary.
            Assert.Equal(0, SqlErrorInfo.Primary([Error(0, @class: 0), Error(3621, @class: 0)]).Number);
        }

        [Theory]
        [InlineData(TellmaSqlErrors.CallerInvalid, TellmaSqlErrors.CallerInvalidCode)]
        [InlineData(TellmaSqlErrors.StaleVersionTag, TellmaSqlErrors.StaleVersionTagCode)]
        [InlineData(TellmaSqlErrors.LimitExceeded, "Delete.CapExceeded")]
        [InlineData(TellmaSqlErrors.Invariant, "Ledger.Unbalanced")]
        [InlineData(TellmaSqlErrors.CountMismatch, "Rows.Count")]
        [InlineData(TellmaSqlErrors.SchemaBehind, TellmaSqlErrors.SchemaBehindCode)]
        [InlineData(TellmaSqlErrors.Transient, "Busy")]
        [InlineData(TellmaSqlErrors.DistroMin, "Distribution.Rule")]
        [InlineData(TellmaSqlErrors.DistroMax, "Distribution.Other")]
        public void An_assigned_number_becomes_an_assertion_with_its_code_and_statement(int number, string code)
        {
            BatchAssertionFailedException failed = Translate<BatchAssertionFailedException>(Error(number, message: code, line: FirstLine));

            Assert.Equal((number, code, 0), (failed.Number, failed.Code, failed.StatementOrdinal));
            Assert.Empty(failed.MissingIds);
        }

        [Fact]
        public void Row_security_tree_errors_and_arithmetic_become_their_own_exceptions()
        {
            Assert.Equal(1, Translate<RowSecurityException>(Error(TellmaSqlErrors.RowSecurity, line: SecondLine)).StatementOrdinal);

            TreeCycleException cycle = Translate<TreeCycleException>(Error(TellmaSqlErrors.Invariant, message: TellmaSqlErrors.TreeCycleCode, line: SecondLine));
            Assert.Equal((typeof(Node), 1), (cycle.EntityType, cycle.StatementOrdinal));

            TreeDepthExceededException depth = Translate<TreeDepthExceededException>(Error(530, line: SecondLine));
            Assert.Equal(typeof(Node), depth.EntityType);

            // A statement that composes for no entity names none.
            Assert.Null(Translate<TreeDepthExceededException>(Error(530, line: FirstLine)).EntityType);

            Assert.Equal(ArithmeticFailureException.DivideByZero, Translate<ArithmeticFailureException>(Error(8134, line: FirstLine)).Reason);
            Assert.Equal(ArithmeticFailureException.Overflow, Translate<ArithmeticFailureException>(Error(8115, line: FirstLine)).Reason);
        }

        [Fact]
        public void A_duplicate_key_and_a_foreign_key_name_their_index_and_constraint_and_a_check_constraint_stays_generic()
        {
            Assert.Equal(
                "PK_Jobs",
                Translate<UniqueConstraintViolationException>(Error(2627, message: "Violation of PRIMARY KEY constraint 'PK_Jobs'. Cannot insert duplicate key in object 'core.Jobs'. The duplicate key value is (5).")).IndexName);
            Assert.Equal(
                "IX_Widgets_Code",
                Translate<UniqueConstraintViolationException>(Error(2601, message: "Cannot insert duplicate key row in object 'fixture.Widgets' with unique index 'IX_Widgets_Code'. The duplicate key value is (A).")).IndexName);
            Assert.Equal(
                "FK_WidgetExtras_Id",
                Translate<ForeignKeyViolationException>(Error(547, message: "The INSERT statement conflicted with the FOREIGN KEY constraint \"FK_WidgetExtras_Id\". The conflict occurred in database \"fx\", table \"fixture.Widgets\", column 'Id'.")).ConstraintName);

            // A check constraint is a defect, not a reference: the generic exception, its message kept.
            DataAccessException check = Translate<DataAccessException>(Error(547, message: "The INSERT statement conflicted with the CHECK constraint \"CK_Jobs_Id\". The conflict occurred in database \"fx\", table \"core.Jobs\", column 'Id'."));
            Assert.Contains("CK_Jobs_Id", check.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Index_names_are_found_in_a_message_of_another_language_by_their_quotes()
        {
            Assert.Equal("PK_Jobs", DataErrors.IndexName(2627, "Verletzung der PRIMARY KEY-Einschränkung 'PK_Jobs'. In das Objekt 'core.Jobs' kann kein doppelter Schlüssel eingefügt werden."));
            Assert.Null(DataErrors.IndexName(2601, "No quoted name."));
            Assert.Null(DataErrors.ForeignKeyName("No quoted name."));
            Assert.Null(DataErrors.ForeignKeyName("Konflikt mit der Einschränkung \"CK_Jobs_Id\"."));
        }

        [Fact]
        public void An_unassigned_platform_number_and_a_number_outside_the_bands_are_defects()
        {
            foreach (int number in new[] { 50450, 50000, 50399, 50700, 60000 })
            {
                InvalidOperationException defect = Translate<InvalidOperationException>(Error(number, line: FirstLine));
                Assert.Contains(number.ToString(System.Globalization.CultureInfo.InvariantCulture), defect.Message, StringComparison.Ordinal);
            }

            // A bump that found no row of its tag is a missing migration, not a user error.
            _ = Translate<InvalidOperationException>(Error(TellmaSqlErrors.Invariant, message: TellmaSqlErrors.VersionTagMissingCode));
        }

        [Fact]
        public void Any_other_error_is_the_generic_exception_and_one_inside_a_procedure_names_no_statement()
        {
            DataAccessException generic = Translate<DataAccessException>(Error(208, message: "Invalid object name 'fixture.Missing'.", line: SecondLine));
            Assert.Equal(1, generic.StatementOrdinal);

            // A trigger's or procedure's line numbers are its own.
            Assert.Null(Translate<DataAccessException>(Error(208, line: SecondLine, procedure: "trg_Widgets")).StatementOrdinal);

            // The frame's own lines resolve to no statement.
            Assert.Null(Translate<BatchAssertionFailedException>(Error(TellmaSqlErrors.SchemaBehind, message: TellmaSqlErrors.SchemaBehindCode, line: 3)).StatementOrdinal);
        }

        [Fact]
        public async Task A_concurrency_conflict_and_a_not_found_check_carry_the_rows_their_statement_reported()
        {
            // --- The conflict rows, read before the THROW ---
            using DataTable conflicts = ConflictTable((7, "C", "Alice"), (9, "M", null));
            using DataTableReader conflictReader = new(conflicts);
            ResultSetCursor withConflicts = new(conflictReader);
            DbDataReader set = Assert.IsAssignableFrom<DbDataReader>(await withConflicts.NextIfAsync(static reader => reader.GetName(1) == "Reason", Token));
            await withConflicts.ReadConflictsAsync(set, Token);

            ConcurrencyConflictException conflict = Assert.IsType<ConcurrencyConflictException>(
                DataErrors.Translate([Error(TellmaSqlErrors.Concurrency, line: FirstLine)], Provider, Assembly, withConflicts));
            Assert.Equal([(7L, false, "Alice"), (9L, true, null)], conflict.Conflicts.Select(static row => (row.Id, row.IsMissing, row.Name)));
            Assert.Equal(0, conflict.StatementOrdinal);

            // --- The missing ids, read before the THROW ---
            using DataTable missing = new();
            _ = missing.Columns.Add("Id", typeof(int));
            _ = missing.Rows.Add(4);
            _ = missing.Rows.Add(6);
            using DataTableReader missingReader = new(missing);
            ResultSetCursor withMissing = new(missingReader);
            await withMissing.ReadMissingIdsAsync(Assert.IsAssignableFrom<DbDataReader>(await withMissing.NextIfAsync(static _ => true, Token)), Token);

            BatchAssertionFailedException notFound = Assert.IsType<BatchAssertionFailedException>(
                DataErrors.Translate([Error(TellmaSqlErrors.NotFound, message: TellmaSqlErrors.NotFoundCode)], Provider, Assembly, withMissing));
            Assert.Equal([4L, 6L], notFound.MissingIds);

            // --- Without the reported rows, each is a defect of the statement's reader ---
            _ = Translate<InvalidOperationException>(Error(TellmaSqlErrors.Concurrency));
            _ = Translate<InvalidOperationException>(Error(TellmaSqlErrors.NotFound, message: TellmaSqlErrors.NotFoundCode));
        }

        /// <summary>Translates one error with no cursor, expecting an exception of exactly one type that carries the provider's.</summary>
        /// <typeparam name="T">The expected type.</typeparam>
        /// <param name="error">The error.</param>
        /// <returns>The translation.</returns>
        private static T Translate<T>(SqlErrorInfo error)
            where T : Exception
        {
            T translated = Assert.IsType<T>(DataErrors.Translate([error], Provider, Assembly, cursor: null));
            Assert.Same(Provider, translated.InnerException);
            return translated;
        }

        /// <summary>A server error.</summary>
        /// <param name="number">The number.</param>
        /// <param name="class">The severity.</param>
        /// <param name="message">The message.</param>
        /// <param name="line">The line in the batch text.</param>
        /// <param name="procedure">The procedure or trigger that raised it, if any.</param>
        /// <returns>The error.</returns>
        private static SqlErrorInfo Error(int number, byte @class = 16, string message = "An error.", int line = 1, string? procedure = null)
        {
            return new SqlErrorInfo(number, @class, message, procedure, line);
        }

        /// <summary>A concurrency guard's conflict set.</summary>
        /// <param name="rows">Each row's id, reason and name.</param>
        /// <returns>The table.</returns>
        private static DataTable ConflictTable(params (long Id, string Reason, string? Name)[] rows)
        {
            DataTable table = new();
            _ = table.Columns.Add("Id", typeof(long));
            _ = table.Columns.Add("Reason", typeof(string));
            _ = table.Columns.Add("ModifiedAt", typeof(DateTimeOffset));
            _ = table.Columns.Add("ModifiedById", typeof(int));
            _ = table.Columns.Add("Name", typeof(string));
            _ = table.Columns.Add("Name2", typeof(string));
            _ = table.Columns.Add("Name3", typeof(string));
            foreach ((long id, string reason, string? name) in rows)
            {
                _ = table.Rows.Add(
                    id,
                    reason,
                    name is null ? DBNull.Value : new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
                    name is null ? DBNull.Value : 3,
                    (object?)name ?? DBNull.Value,
                    DBNull.Value,
                    DBNull.Value);
            }

            return table;
        }

        /// <summary>An entity a statement composes for.</summary>
        private sealed class Node;
    }
}
