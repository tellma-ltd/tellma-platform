// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Collections.Immutable;
using System.Globalization;
using Tellma.Core.Analyzers.Entities;
using Tellma.Core.Analyzers.Sql;

namespace Tellma.Core.Analyzers
{
    /// <summary>
    ///     TELLMA0003 and TELLMA0006 over <c>IDataBatch</c>: the raw statement text of every
    ///     <c>Sql</c> call, parsed with ScriptDom and checked against the batch's rules, and every
    ///     write into a mapped entity's table (raw text, <c>Save</c>, <c>Update</c>, <c>Delete</c>)
    ///     from a type that is not the table's owner.
    /// </summary>
    /// <remarks>
    ///     The text is read from the interpolated string at the call, with a stand-in for each hole: a
    ///     quoted name for an identifier whose name the code spells, a bracketed stand-in for one it
    ///     does not, and a variable for everything else. A write whose table the code does not show (an
    ///     identifier hole of unknown name, a type parameter without an entity constraint) is left to
    ///     the batch's run-time checks.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DataBatchAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The lowest number a statement may throw: the platform band's start.</summary>
        private const long LowestThrow = 50400;

        /// <summary>The highest number a statement may throw: the distribution band's end.</summary>
        private const long HighestThrow = 50699;

        /// <summary>The highest severity <c>RAISERROR</c> may raise: informational messages only.</summary>
        private const long HighestRaiseSeverity = 10;

        /// <summary>The longest hole expression a message quotes.</summary>
        private const int QuotedExpressionLength = 60;

        /// <summary>The batch's entity writes, one type argument each.</summary>
        private static readonly ImmutableHashSet<string> EntityWrites = ImmutableHashSet.Create(StringComparer.Ordinal, "Save", "Update", "Delete");

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [TellmaDiagnostics.RawSql, TellmaDiagnostics.OwnedTableWrite];

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(static start =>
            {
                // --- Per compilation: whether it is the runtime, and the entity tables when first needed ---
                bool runtime = string.Equals(start.Compilation.AssemblyName, KnownNames.RuntimeAssembly, StringComparison.Ordinal);
                Compilation compilation = start.Compilation;
                Lazy<EntityTables> tables = new(() => EntityTables.Build(compilation), LazyThreadSafetyMode.ExecutionAndPublication);
                Scope scope = new(runtime, tables);

                start.RegisterOperationAction(operation => AnalyzeInvocation(operation, scope), OperationKind.Invocation);
                start.RegisterOperationAction(operation => AnalyzeMethodReference(operation, scope), OperationKind.MethodReference);
            });
        }

        // ------------------------------------------------------------------
        // Entry points
        // ------------------------------------------------------------------

        /// <summary>Checks a call to the batch's raw statement or one of its entity writes.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="scope">The compilation's scope.</param>
        private static void AnalyzeInvocation(OperationAnalysisContext context, Scope scope)
        {
            var invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol method = invocation.TargetMethod;
            if (IsRawStatement(method))
            {
                AnalyzeRawStatement(context, scope, invocation);
            }
            else if (IsEntityWrite(method))
            {
                AnalyzeEntityWrite(context, scope, method, invocation.Syntax);
            }
        }

        /// <summary>Checks the batch's raw statement or an entity write taken as a delegate.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="scope">The compilation's scope.</param>
        private static void AnalyzeMethodReference(OperationAnalysisContext context, Scope scope)
        {
            var reference = (IMethodReferenceOperation)context.Operation;
            if (reference.IsInsideNameOf())
            {
                return;
            }

            if (IsRawStatement(reference.Method))
            {
                Report(context, TellmaDiagnostics.RawSql, reference.Syntax.GetLocation(),
                    "IDataBatch.Sql is taken as a delegate, so the text it is later called with cannot be checked. Call IDataBatch.Sql directly with the statement written at the call.");
            }
            else if (IsEntityWrite(reference.Method))
            {
                AnalyzeEntityWrite(context, scope, reference.Method, reference.Syntax);
            }
        }

        /// <summary>Whether a method is the batch's raw statement, <c>Sql(FormattableString, SqlOptions?)</c>.</summary>
        /// <param name="method">The method.</param>
        /// <returns>True when it is.</returns>
        private static bool IsRawStatement(IMethodSymbol method)
        {
            return method is { Name: "Sql", Parameters.Length: 2 }
                && method.Parameters[0].Type.Is("System", "FormattableString")
                && method.Parameters[1].Type.Is(KnownNames.DataNamespace, KnownNames.SqlOptions)
                && method.IsOnTypeImplementing(KnownNames.DataNamespace, KnownNames.DataBatch);
        }

        /// <summary>Whether a method is one of the batch's entity writes, <c>Save&lt;T&gt;</c>, <c>Update&lt;T&gt;</c> or <c>Delete&lt;T&gt;</c>.</summary>
        /// <param name="method">The method.</param>
        /// <returns>True when it is.</returns>
        private static bool IsEntityWrite(IMethodSymbol method)
        {
            return method.TypeArguments.Length == 1
                && EntityWrites.Contains(method.Name)
                && method.IsOnTypeImplementing(KnownNames.DataNamespace, KnownNames.DataBatch);
        }

        // ------------------------------------------------------------------
        // TELLMA0006 over the entity writes
        // ------------------------------------------------------------------

        /// <summary>Reports an entity write into a stack-owned table from outside its stack.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="scope">The compilation's scope.</param>
        /// <param name="method">The write.</param>
        /// <param name="syntax">Where.</param>
        private static void AnalyzeEntityWrite(OperationAnalysisContext context, Scope scope, IMethodSymbol method, SyntaxNode syntax)
        {
            if (scope.Runtime || EntityTables.TableOf(method.TypeArguments[0]) is not TableKey table)
            {
                return;
            }

            if (scope.Tables.Value.Find(table) is EntityTable entry && !Owns(context.ContainingSymbol, entry))
            {
                Report(context, TellmaDiagnostics.OwnedTableWrite, syntax.GetLocation(),
                    $"IDataBatch.{method.Name}<{method.TypeArguments[0].Name}> writes {table}, the table of {entry.Entity.Name}, from a type that is not its owner. A stack-owned table is written by its own service, validators, effects and companions; enlist the write with the owning stack through IEnlists<T> instead.");
            }
        }

        /// <summary>
        ///     Whether the code's enclosing types own a table: a type deriving from
        ///     <c>EntityService&lt;,&gt;</c>, or implementing <c>IEntityValidator&lt;&gt;</c> or
        ///     <c>IPersistEffect&lt;&gt;</c>, over an entity whose table is the table or one of its owners.
        /// </summary>
        /// <param name="symbol">The analysis context's containing symbol.</param>
        /// <param name="entry">The written table.</param>
        /// <returns>True when they do.</returns>
        private static bool Owns(ISymbol symbol, EntityTable entry)
        {
            foreach (INamedTypeSymbol type in symbol.EnclosingTypes())
            {
                foreach (ITypeSymbol entity in OwnedEntities(type))
                {
                    if (EntityTables.TableOf(entity) is TableKey owned && (owned.Equals(entry.Key) || entry.Owners.Contains(owned)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The entity types a type serves as a pipeline participant.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The entity type arguments.</returns>
        private static IEnumerable<ITypeSymbol> OwnedEntities(INamedTypeSymbol type)
        {
            if (type.FindSelfOrBase(KnownNames.CrudNamespace, KnownNames.EntityService) is INamedTypeSymbol service)
            {
                yield return service.TypeArguments[0];
            }

            foreach (INamedTypeSymbol participant in type.FindInterfaces(KnownNames.CrudNamespace, KnownNames.EntityValidator)
                .Concat(type.FindInterfaces(KnownNames.CrudNamespace, KnownNames.PersistEffect)))
            {
                yield return participant.TypeArguments[0];
            }
        }

        // ------------------------------------------------------------------
        // TELLMA0003 (and TELLMA0006) over the raw statement
        // ------------------------------------------------------------------

        /// <summary>Checks one raw statement: its text, its holes, its statements and its declared writes.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="scope">The compilation's scope.</param>
        /// <param name="invocation">The call.</param>
        private static void AnalyzeRawStatement(OperationAnalysisContext context, Scope scope, IInvocationOperation invocation)
        {
            IMethodSymbol method = invocation.TargetMethod;
            if (invocation.Arguments.ArgumentFor(method.Parameters[0].Name) is not IOperation argument)
            {
                return;
            }

            // --- The text must be written at the call ---
            if (SqlText.From(argument) is not SqlText text)
            {
                Report(context, TellmaDiagnostics.RawSql, argument.Syntax.GetLocation(),
                    "The statement text is not an interpolated string written at the call, so it cannot be parsed and checked. Write it as an interpolated string in the IDataBatch.Sql call itself; vary names with SqlIdentifier holes, never with concatenation or a FormattableString held elsewhere.");
                return;
            }

            CheckHoles(context, text);

            // --- The text must parse, as one batch ---
            var inspector = SqlInspector.Inspect(text.Text);
            if (inspector.Errors.Count > 0)
            {
                ParseError error = inspector.Errors[0];
                Report(context, TellmaDiagnostics.RawSql, text.LocationOf(error.Offset, 1),
                    $"The statement text does not parse as T-SQL at line {error.Line.ToString(CultureInfo.InvariantCulture)}, column {error.Column.ToString(CultureInfo.InvariantCulture)}: {error.Message}");
                return;
            }

            if (inspector.BatchCount > 1)
            {
                Report(context, TellmaDiagnostics.RawSql, text.Location,
                    "The statement text holds more than one batch: GO separates batches in client tools and is not T-SQL. One IDataBatch.Sql call holds one batch's statements.");
            }

            CheckQuotedHoles(context, text, inspector);
            CheckStatements(context, text, inspector);
            CheckVariables(context, scope, text, inspector);
            CheckRaises(context, text, inspector);
            CheckWrites(context, scope, text, inspector, SqlOptionsShape.From(invocation.Arguments.ArgumentFor(method.Parameters[1].Name)));
        }

        /// <summary>Reports the holes the batch cannot bind as meant.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="text">The text.</param>
        private static void CheckHoles(OperationAnalysisContext context, SqlText text)
        {
            foreach (SqlHole hole in text.Holes)
            {
                string? message = hole.Kind switch
                {
                    SqlHoleKind.Formatted =>
                        $"The hole {{{Quote(hole)}}} carries an alignment or a format string, which the batch refuses: a value binds as a parameter, never as formatted text. Remove the ',' or ':' part.",
                    SqlHoleKind.NameOf =>
                        $"The hole {{{Quote(hole)}}} binds the name as a string parameter, not as a name. Write a column or table name as {{new SqlIdentifier(nameof(...))}}.",
                    SqlHoleKind.Unsupported =>
                        $"The hole {{{Quote(hole)}}} is of type {hole.TypeName ?? "unknown"}, which the batch cannot bind. A hole is a scalar value, a SqlIdentifier, or a list of table-type rows.",
                    SqlHoleKind.Value or SqlHoleKind.Identifier or SqlHoleKind.Table => null,
                    _ => null,
                };
                if (message is not null)
                {
                    Report(context, TellmaDiagnostics.RawSql, hole.Location, message);
                }
            }
        }

        /// <summary>Reports holes written inside a string literal, a comment or a quoted name, where nothing binds them.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="text">The text.</param>
        /// <param name="inspector">The parsed text.</param>
        private static void CheckQuotedHoles(OperationAnalysisContext context, SqlText text, SqlInspector inspector)
        {
            foreach (TSqlParserToken token in inspector.Tokens)
            {
                bool quotedName = token.TokenType == TSqlTokenType.QuotedIdentifier;
                if (!quotedName
                    && token.TokenType is not (TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment))
                {
                    continue;
                }

                int start = token.Offset;
                int end = token.Offset + token.Text.Length;
                foreach (SqlHole hole in text.Holes)
                {
                    (int holeStart, int holeEnd) = text.SpanOf(hole);

                    // A quoted name the hole itself emitted lies within the hole; any other overlap puts the hole inside quotes.
                    bool overlaps = holeStart < end && start < holeEnd;
                    bool emitted = quotedName && holeStart <= start && end <= holeEnd;
                    if (overlaps && !emitted)
                    {
                        Report(context, TellmaDiagnostics.RawSql, hole.Location,
                            $"The hole {{{Quote(hole)}}} sits inside a string literal, a comment or a quoted name, where its parameter or name is not substituted as meant. Write the hole where a value or a name goes, outside quotes.");
                    }
                }
            }
        }

        /// <summary>Reports the statements the text may not hold, and the executions outside the allow-list.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="text">The text.</param>
        /// <param name="inspector">The parsed text.</param>
        private static void CheckStatements(OperationAnalysisContext context, SqlText text, SqlInspector inspector)
        {
            foreach (SqlRefusal refusal in inspector.Refusals)
            {
                Report(context, TellmaDiagnostics.RawSql, LocationOf(text, refusal.Fragment),
                    $"{refusal.What} is refused in IDataBatch.Sql text. {refusal.Why}");
            }

            foreach (SqlExecution execution in inspector.Executions)
            {
                if (execution.IsAllowed)
                {
                    continue;
                }

                Report(context, TellmaDiagnostics.RawSql, LocationOf(text, execution.Fragment), execution.Name is null
                    ? "Dynamic SQL (EXEC of a string or of a procedure variable) is refused in IDataBatch.Sql text, because its statements cannot be checked. Write the statements in the text itself."
                    : $"EXEC {execution.Name} is refused in IDataBatch.Sql text: a statement executes only sys.sp_sequence_get_range and sys.sp_getapplock. Write the statements in the text itself.");
            }
        }

        /// <summary>Reports reserved names declared or assigned, and holes declared or assigned as variables.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="scope">The compilation's scope.</param>
        /// <param name="text">The text.</param>
        /// <param name="inspector">The parsed text.</param>
        private static void CheckVariables(OperationAnalysisContext context, Scope scope, SqlText text, SqlInspector inspector)
        {
            // --- The names written: declarations, assignments, and table variables a statement writes ---
            IEnumerable<SqlVariableUse> uses = inspector.Variables.Concat(inspector.Targets
                .Where(static target => target.Variable is not null)
                .Select(static target => new SqlVariableUse(target.Variable!, Declared: false, target.Fragment)));

            foreach (SqlVariableUse use in uses)
            {
                string verb = use.Declared ? "declares" : "assigns";
                if (text.HoleNamed(use.Name) is SqlHole hole)
                {
                    // A value hole is the batch's own parameter; a batch table may be filled, but no hole is declared.
                    if (use.Declared || hole.Kind == SqlHoleKind.Value)
                    {
                        Report(context, TellmaDiagnostics.RawSql, hole.Location,
                            $"The text {verb} the hole {{{Quote(hole)}}}, which the batch binds as its own parameter. Declare a variable of the statement's own and assign that instead.");
                    }
                }
                else if (!scope.Runtime && ReservedNames.IsReserved(use.Name))
                {
                    Report(context, TellmaDiagnostics.RawSql, LocationOf(text, use.Fragment),
                        $"The text {verb} {use.Name}, a name reserved for the platform's own parameters and tables (@tm_, and @qx or @tb followed by digits and _). Choose another name.");
                }
            }
        }

        /// <summary>Reports a <c>THROW</c> outside the bands and a <c>RAISERROR</c> above the informational severities.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="text">The text.</param>
        /// <param name="inspector">The parsed text.</param>
        private static void CheckRaises(OperationAnalysisContext context, SqlText text, SqlInspector inspector)
        {
            foreach (SqlRaise raise in inspector.Raises)
            {
                Location location = LocationOf(text, raise.Fragment);
                if (raise.IsThrow)
                {
                    // A bare THROW rethrows inside CATCH; a number the text does not fix cannot be judged here.
                    if (raise.Value is not null && ValueOf(raise.Value, text) is long number && number is < LowestThrow or > HighestThrow)
                    {
                        Report(context, TellmaDiagnostics.RawSql, location,
                            $"THROW {number.ToString(CultureInfo.InvariantCulture)} is outside the platform's error bands. A statement throws a number from 50400 to 50599 (the platform's) or from 50600 to 50699 (the distribution's), which the batch maps to its exceptions.");
                    }
                }
                else if (raise.Value is null || ValueOf(raise.Value, text) is not long severity || severity is < 0 or > HighestRaiseSeverity)
                {
                    Report(context, TellmaDiagnostics.RawSql, location,
                        "RAISERROR above severity 10, or at a severity the text does not fix, is refused. Raise an error with THROW and a number from the platform's or the distribution's band; RAISERROR is kept for informational messages at a constant severity of 10 or less.");
                }
            }
        }

        /// <summary>
        ///     Reports permanent tables written without declared writes or outside the declared list,
        ///     writes into user-tagged tables that name no users, and writes into stack-owned tables
        ///     from outside their stack.
        /// </summary>
        /// <param name="context">The operation context.</param>
        /// <param name="scope">The compilation's scope.</param>
        /// <param name="text">The text.</param>
        /// <param name="inspector">The parsed text.</param>
        /// <param name="options">What the options show.</param>
        private static void CheckWrites(OperationAnalysisContext context, Scope scope, SqlText text, SqlInspector inspector, SqlOptionsShape options)
        {
            // --- The permanent tables written, each once, and the writes the text does not reduce to a table ---
            Dictionary<TableKey, SqlTarget> permanent = [];
            foreach (SqlTarget target in inspector.Targets)
            {
                if (target is { Kind: TargetKind.Permanent, Schema: string schema, Name: string name })
                {
                    TableKey key = new(schema, name);
                    if (!permanent.ContainsKey(key))
                    {
                        permanent[key] = target;
                    }
                }
            }

            SqlTarget? unresolved = inspector.Targets.FirstOrDefault(static target => target.Kind == TargetKind.Unresolved);

            // --- Writes must be declared, and must list what the text writes ---
            if (options.Writes == WritesShape.Absent)
            {
                foreach (KeyValuePair<TableKey, SqlTarget> written in permanent)
                {
                    Report(context, TellmaDiagnostics.RawSql, LocationOf(text, written.Value.Fragment),
                        $"The text writes {written.Key} ({written.Value.Statement}) but its SqlOptions declare no writes. Declare every table the statement writes in SqlOptions.Writes, so the batch checks its write authority and bumps the tables' version tags.");
                }

                if (unresolved is not null)
                {
                    Report(context, TellmaDiagnostics.RawSql, LocationOf(text, unresolved.Fragment),
                        $"The text writes a table through a common table expression or a derived table ({unresolved.Statement}) but its SqlOptions declare no writes. Declare every table the statement writes in SqlOptions.Writes.");
                }
            }
            else if (options.Writes == WritesShape.Listed)
            {
                HashSet<TableKey> listed = [.. options.Listed];
                foreach (KeyValuePair<TableKey, SqlTarget> written in permanent)
                {
                    if (!listed.Contains(written.Key))
                    {
                        Report(context, TellmaDiagnostics.RawSql, LocationOf(text, written.Value.Fragment),
                            $"The text writes {written.Key} ({written.Value.Statement}), which its SqlOptions.Writes does not list. Declare every table the statement writes.");
                    }
                }
            }

            if (permanent.Count == 0 && options.Listed.Count == 0)
            {
                return;
            }

            EntityTables tables = scope.Tables.Value;

            // --- A write into a table with a user-level tag rule names the users it wrote ---
            if (options.Writes != WritesShape.Absent && !options.NamesUsers)
            {
                foreach (TableKey table in options.Listed.Concat(permanent.Keys).Distinct())
                {
                    if (tables.Find(table) is { BumpsUserTag: true } entry)
                    {
                        Report(context, TellmaDiagnostics.RawSql, text.Location,
                            $"The statement writes {table}, whose writes bump a user-level version tag of each user they name ({entry.Entity.Name}), but its SqlOptions name no UserIds. Pass the affected user ids in SqlOptions.UserIds, or use SqlOptions.ForCaller for the caller's own rows.");
                        break;
                    }
                }
            }

            // --- TELLMA0006: a stack-owned table is written by its owner ---
            if (scope.Runtime)
            {
                return;
            }

            foreach (KeyValuePair<TableKey, SqlTarget> written in permanent)
            {
                if (tables.Find(written.Key) is EntityTable entry && !Owns(context.ContainingSymbol, entry))
                {
                    Report(context, TellmaDiagnostics.OwnedTableWrite, LocationOf(text, written.Value.Fragment),
                        $"The text writes {written.Key} ({written.Value.Statement}), the table of {entry.Entity.Name}, from a type that is not its owner. A stack-owned table is written by its own service, validators, effects and companions; enlist the write with the owning stack through IEnlists<T> instead.");
                }
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>The integer a scalar expression fixes: a literal, a signed or parenthesised one, or a constant hole.</summary>
        /// <param name="expression">The expression.</param>
        /// <param name="text">The text, for its holes.</param>
        /// <returns>The value, or null when the text does not fix one.</returns>
        private static long? ValueOf(ScalarExpression expression, SqlText text)
        {
            return expression switch
            {
                IntegerLiteral literal when long.TryParse(literal.Value, NumberStyles.None, CultureInfo.InvariantCulture, out long value) => value,
                UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative } negative => -ValueOf(negative.Expression, text),
                UnaryExpression { UnaryExpressionType: UnaryExpressionType.Positive } positive => ValueOf(positive.Expression, text),
                ParenthesisExpression parenthesis => ValueOf(parenthesis.Expression, text),

                // A constant hole binds as a parameter holding its value.
                VariableReference variable when text.HoleNamed(variable.Name) is SqlHole { Constant.HasValue: true } hole => hole.Constant.Value switch
                {
                    int number => number,
                    long number => number,
                    short number => number,
                    byte number => number,
                    _ => null,
                },
                _ => null,
            };
        }

        /// <summary>The source location of a parsed fragment.</summary>
        /// <param name="text">The text.</param>
        /// <param name="fragment">The fragment.</param>
        /// <returns>The location.</returns>
        private static Location LocationOf(SqlText text, TSqlFragment fragment)
        {
            return text.LocationOf(fragment.StartOffset, fragment.FragmentLength);
        }

        /// <summary>A hole's expression as a message quotes it.</summary>
        /// <param name="hole">The hole.</param>
        /// <returns>The expression's source, shortened.</returns>
        private static string Quote(SqlHole hole)
        {
            string source = hole.Value.Syntax.ToString();
            return source.Length <= QuotedExpressionLength ? source : source.Substring(0, QuotedExpressionLength) + "...";
        }

        /// <summary>Reports one diagnostic.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="descriptor">The rule.</param>
        /// <param name="location">Where.</param>
        /// <param name="message">The sentence that says what was refused and what to write instead.</param>
        private static void Report(OperationAnalysisContext context, DiagnosticDescriptor descriptor, Location location, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, message));
        }

        /// <summary>What a compilation's checks share.</summary>
        /// <param name="runtime">Whether the compilation is the runtime, which may use the reserved names and write every table.</param>
        /// <param name="tables">The compilation's entity tables, built when first needed.</param>
        private sealed class Scope(bool runtime, Lazy<EntityTables> tables)
        {
            /// <summary>Whether the compilation is the runtime.</summary>
            public bool Runtime { get; } = runtime;

            /// <summary>The compilation's entity tables.</summary>
            public Lazy<EntityTables> Tables { get; } = tables;
        }
    }
}
