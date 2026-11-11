// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    // The frame follows spec 0012 §5.3 and §5.5.
    /// <summary>
    ///     A batch assembled into one command: the framed text, every parameter, the statements in
    ///     emission order and the line each statement starts on, so a server error's line number
    ///     resolves to the statement that raised it.
    /// </summary>
    /// <remarks>
    ///     <para>The frame, in order:</para>
    ///     <list type="number">
    ///         <item><c>SET NOCOUNT ON; SET XACT_ABORT ON;</c> and the schema guard.</item>
    ///         <item>The prologue statements, autocommit.</item>
    ///         <item>
    ///             <c>IF @tm_Guard = 1 BEGIN</c>, on a batch that carries the connect prologue and has a
    ///             body to wrap; T-SQL refuses an empty block, so a prologue-only batch has no wrapper.
    ///         </item>
    ///         <item><c>BEGIN TRANSACTION;</c>, on an <see cref="TransactionMode.Auto" /> batch that declares a write.</item>
    ///         <item>The transaction-head statements, the body statements and the epilogue statements.</item>
    ///         <item><c>COMMIT TRANSACTION;</c>, then <c>END;</c>, closing what steps 3 and 4 opened.</item>
    ///     </list>
    /// </remarks>
    internal sealed class BatchAssembly
    {
        /// <summary>The most parameters one batch may declare; a table-valued parameter counts as one.</summary>
        public const int MaxParameters = 2000;

        /// <summary>The frame's opening session settings.</summary>
        public const string SessionSettings = "SET NOCOUNT ON;\nSET XACT_ABORT ON;";

        /// <summary>The schema guard, the first statement of every round trip.</summary>
        public static readonly string SchemaGuardText = string.Create(
            CultureInfo.InvariantCulture,
            $"IF NOT EXISTS (SELECT 1 FROM [{PlatformTables.Schema}].[{TellmaSchemaRowConfiguration.Table}] WHERE [Fingerprint] = {ReservedSqlNames.SchemaFingerprint})\n    THROW {TellmaSqlErrors.SchemaBehind}, N'{TellmaSqlErrors.SchemaBehindCode}', 1;");

        /// <summary>The guard wrapper's opening, on a batch that carries the connect prologue.</summary>
        public static readonly string GuardOpen = $"IF {ReservedSqlNames.Guard} = 1\nBEGIN";

        /// <summary>The guard wrapper's closing.</summary>
        public const string GuardClose = "END;";

        /// <summary>The in-text transaction's opening.</summary>
        public const string TransactionOpen = "BEGIN TRANSACTION;";

        /// <summary>The in-text transaction's commit, the last statement inside it.</summary>
        public const string TransactionCommit = "COMMIT TRANSACTION;";

        /// <summary>Each text statement's first and last line, with its ordinal, in emission order.</summary>
        private readonly List<(int First, int Last, int Ordinal)> _lines;

        /// <summary>Creates the assembly; see <see cref="Create" />.</summary>
        /// <param name="text">The framed text.</param>
        /// <param name="parameters">Every parameter.</param>
        /// <param name="statements">The statements in emission order.</param>
        /// <param name="lines">Each text statement's line range.</param>
        /// <param name="guarded">Whether the body is wrapped in the guard.</param>
        /// <param name="inTextTransaction">Whether the text opens and commits its own transaction.</param>
        private BatchAssembly(
            string text,
            IReadOnlyList<BatchParameter> parameters,
            IReadOnlyList<BatchStatement> statements,
            List<(int First, int Last, int Ordinal)> lines,
            bool guarded,
            bool inTextTransaction)
        {
            Text = text;
            Parameters = parameters;
            Statements = statements;
            _lines = lines;
            Guarded = guarded;
            InTextTransaction = inTextTransaction;
        }

        /// <summary>The framed text.</summary>
        public string Text { get; }

        /// <summary>Every parameter the command carries, the schema fingerprint first.</summary>
        public IReadOnlyList<BatchParameter> Parameters { get; }

        /// <summary>The statements in emission order.</summary>
        public IReadOnlyList<BatchStatement> Statements { get; }

        /// <summary>
        ///     Whether the batch carries the connect prologue, so the body runs only when the guard
        ///     passed. Its text wraps the body in <c>IF @tm_Guard = 1</c> when there is a body to wrap.
        /// </summary>
        public bool Guarded { get; }

        /// <summary>Whether the text opens and commits its own transaction.</summary>
        public bool InTextTransaction { get; }

        /// <summary>How many statements carry text, the frame's own excluded.</summary>
        public int StatementCount => _lines.Count;

        /// <summary>How many rows the table-valued parameters bind.</summary>
        public long TableRows => Parameters.Sum(static parameter => (long)parameter.Rows);

        /// <summary>Assembles statements into one framed command.</summary>
        /// <param name="statements">The statements, in any order; emitted by section, then by ordinal.</param>
        /// <param name="fingerprint">The running model's storage fingerprint, bound as <c>@tm_schema</c>.</param>
        /// <param name="guarded">Whether the batch carries the connect prologue and its guard.</param>
        /// <param name="inTextTransaction">Whether the text opens and commits its own transaction.</param>
        /// <returns>The assembly.</returns>
        /// <exception cref="InvalidOperationException">
        ///     Two statements bind one reserved name to different values, or the batch declares more
        ///     than <see cref="MaxParameters" /> parameters.
        /// </exception>
        public static BatchAssembly Create(IReadOnlyList<BatchStatement> statements, string fingerprint, bool guarded, bool inTextTransaction)
        {
            ArgumentNullException.ThrowIfNull(statements);
            ArgumentException.ThrowIfNullOrEmpty(fingerprint);

            List<BatchStatement> ordered = [.. statements.OrderBy(static statement => statement.Section).ThenBy(static statement => statement.Ordinal)];

            // --- The parameters: the fingerprint, then each statement's; a reserved name bound twice must bind one value ---
            List<BatchParameter> parameters = [SchemaFingerprintParameter(fingerprint)];
            Dictionary<string, BatchParameter> byName = new(StringComparer.OrdinalIgnoreCase) { [parameters[0].Name] = parameters[0] };
            foreach (BatchParameter parameter in ordered.SelectMany(static statement => statement.Parameters))
            {
                if (byName.TryGetValue(parameter.Name, out BatchParameter? bound))
                {
                    if (!bound.BindsSame(parameter))
                    {
                        throw new InvalidOperationException(
                            $"Two statements of the batch bind the parameter '{parameter.Name}' to different values. A batch-wide name binds one value.");
                    }

                    continue;
                }

                byName.Add(parameter.Name, parameter);
                parameters.Add(parameter);
            }

            if (parameters.Count > MaxParameters)
            {
                throw TooManyParameters(parameters.Count);
            }

            // --- The text, with each statement's line range ---
            TextWriter writer = new();
            List<(int First, int Last, int Ordinal)> lines = [];
            writer.Line(SessionSettings);
            writer.Line(SchemaGuardText);
            foreach (BatchStatement statement in ordered.Where(static statement => statement.Section == BatchSection.Prologue))
            {
                writer.Statement(statement, lines);
            }

            // The wrapper only around a body with text: an empty BEGIN … END block does not compile.
            bool wrapped = guarded && (inTextTransaction || ordered.Any(static statement => statement.Section != BatchSection.Prologue && statement.Text.Length > 0));
            if (wrapped)
            {
                writer.Line(GuardOpen);
            }

            if (inTextTransaction)
            {
                writer.Line(TransactionOpen);
            }

            foreach (BatchStatement statement in ordered.Where(static statement => statement.Section != BatchSection.Prologue))
            {
                writer.Statement(statement, lines);
            }

            if (inTextTransaction)
            {
                writer.Line(TransactionCommit);
            }

            if (wrapped)
            {
                writer.Line(GuardClose);
            }

            return new BatchAssembly(writer.ToString(), parameters, ordered, lines, guarded, inTextTransaction);
        }

        /// <summary>The refusal of a batch with too many parameters: a platform defect, because lists ride table-valued parameters.</summary>
        /// <param name="count">The parameters declared.</param>
        /// <returns>The exception.</returns>
        public static InvalidOperationException TooManyParameters(int count)
        {
            return new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The batch declares {count} parameters, more than the {MaxParameters} one batch may declare. Bind lists through table-valued parameters, or split the work across batches."));
        }

        /// <summary>The ordinal of the statement a server error's line number falls in.</summary>
        /// <param name="lineNumber">The error's line number in the batch text, from 1.</param>
        /// <returns>The statement's ordinal, or null for a line of the frame.</returns>
        public int? StatementOrdinalAt(int lineNumber)
        {
            foreach ((int first, int last, int ordinal) in _lines)
            {
                if (lineNumber >= first && lineNumber <= last)
                {
                    return ordinal;
                }
            }

            return null;
        }

        /// <summary>
        ///     Hands the round trip's result sets to the statements in emission order. On a guarded
        ///     batch the body's sets exist only when the guard passed, so its statements are read only
        ///     then; a statement that is not read keeps an incomplete result.
        /// </summary>
        /// <param name="cursor">The round trip's cursor.</param>
        /// <param name="guardVerdict">The verdict the connect prologue's reader reported, or null if none did.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>Whether the body ran.</returns>
        /// <exception cref="InvalidOperationException">
        ///     The batch is guarded but no prologue statement reported the verdict, or the result-set
        ///     counts disagree.
        /// </exception>
        public async ValueTask<bool> ReadAsync(ResultSetCursor cursor, Func<bool?> guardVerdict, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(cursor);
            ArgumentNullException.ThrowIfNull(guardVerdict);

            bool? bodyRuns = null;
            foreach (BatchStatement statement in Statements)
            {
                // The prologue's sets precede the body's; the verdict is known once they are read.
                if (statement.Section != BatchSection.Prologue)
                {
                    bodyRuns ??= BodyRuns(guardVerdict);
                    if (bodyRuns == false)
                    {
                        break;
                    }
                }

                if (statement.Reader is StatementReader reader)
                {
                    await reader(cursor, cancellationToken).ConfigureAwait(false);
                }
            }

            bodyRuns ??= BodyRuns(guardVerdict);
            await cursor.EndAsync(cancellationToken).ConfigureAwait(false);
            return bodyRuns.Value;
        }

        /// <summary>Whether the body ran: always on an unguarded batch, else as the connect prologue reported.</summary>
        /// <param name="guardVerdict">The reported verdict.</param>
        /// <returns>True when it ran.</returns>
        private bool BodyRuns(Func<bool?> guardVerdict)
        {
            return !Guarded || (guardVerdict() ?? throw new InvalidOperationException(
                "The batch carries the connect prologue, but no prologue statement reported the guard's verdict. The connect prologue's reader must report it."));
        }

        /// <summary>The schema fingerprint, bound once per batch as fixed-length text to match the column.</summary>
        /// <param name="fingerprint">The fingerprint.</param>
        /// <returns>The parameter.</returns>
        private static ScalarParameter SchemaFingerprintParameter(string fingerprint)
        {
            return new ScalarParameter(ReservedSqlNames.SchemaFingerprint, SqlDbType.Char, fingerprint, fingerprint.Length);
        }

        /// <summary>Writes the text and counts its lines.</summary>
        private sealed class TextWriter
        {
            /// <summary>The text so far.</summary>
            private readonly StringBuilder _text = new();

            /// <summary>The line the next character lands on, from 1.</summary>
            private int _line = 1;

            /// <summary>Writes a line of the frame.</summary>
            /// <param name="text">The text, without the line break.</param>
            public void Line(string text)
            {
                Append(text);
                Append("\n");
            }

            /// <summary>
            ///     Writes a statement on lines of its own, terminated with a semicolon when its text is
            ///     not, so that the next statement may be one that needs the terminator before it.
            /// </summary>
            /// <param name="statement">The statement.</param>
            /// <param name="lines">The line ranges, appended to.</param>
            public void Statement(BatchStatement statement, List<(int First, int Last, int Ordinal)> lines)
            {
                if (statement.Text.Length == 0)
                {
                    // A statement that only binds parameters, such as a table-valued parameter.
                    return;
                }

                int first = _line;
                Append(statement.Text);

                // On a line of its own, so a trailing line comment cannot swallow it. A text that ends
                // in ';' needs none, unless that ';' may sit in a line comment on the last line; an
                // extra empty statement is legal T-SQL, so the doubt is resolved by adding one.
                ReadOnlySpan<char> trimmed = statement.Text.AsSpan().TrimEnd();
                int lastLine = trimmed.LastIndexOf('\n') + 1;
                if (!trimmed.EndsWith(";", StringComparison.Ordinal) || trimmed[lastLine..].Contains("--", StringComparison.Ordinal))
                {
                    Append("\n;");
                }

                lines.Add((first, _line, statement.Ordinal));
                Append("\n");
            }

            /// <inheritdoc />
            public override string ToString()
            {
                return _text.ToString();
            }

            /// <summary>Appends text, counting its line breaks.</summary>
            /// <param name="text">The text.</param>
            private void Append(string text)
            {
                _ = _text.Append(text);
                _line += text.AsSpan().Count('\n');
            }
        }
    }
}
