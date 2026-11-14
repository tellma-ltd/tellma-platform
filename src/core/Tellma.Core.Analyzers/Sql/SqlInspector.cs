// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Tellma.Core.Analyzers.Sql
{
    /// <summary>What a statement's written table is, as far as its text shows.</summary>
    internal enum TargetKind
    {
        /// <summary>A permanent table named in the text, or by an identifier hole whose name the code shows.</summary>
        Permanent,

        /// <summary>A table variable, a temporary table, or a table the batch declared: no permanent table is written.</summary>
        Local,

        /// <summary>An identifier hole whose name the code does not show.</summary>
        UnknownHole,

        /// <summary>A common table expression or a derived table the text does not reduce to one table.</summary>
        Unresolved,
    }

    /// <summary>One table a statement writes.</summary>
    /// <param name="Statement">The writing statement's keyword, for messages.</param>
    /// <param name="Kind">What the table is.</param>
    /// <param name="Schema">A permanent table's schema, <c>dbo</c> when the text names none.</param>
    /// <param name="Name">A permanent table's name.</param>
    /// <param name="Variable">A table variable's name, with its <c>@</c>.</param>
    /// <param name="Fragment">Where the text names the table.</param>
    internal sealed record SqlTarget(string Statement, TargetKind Kind, string? Schema, string? Name, string? Variable, TSqlFragment Fragment);

    /// <summary>One procedure the text executes, or one dynamic execution.</summary>
    /// <param name="Name">The procedure's name as written, or null when the executed text or procedure is dynamic.</param>
    /// <param name="IsAllowed">Whether it is one of the two allow-listed system procedures.</param>
    /// <param name="Fragment">Where.</param>
    internal sealed record SqlExecution(string? Name, bool IsAllowed, TSqlFragment Fragment);

    /// <summary>A statement the text may not hold at all, with its description.</summary>
    /// <param name="What">The statement, as a message names it.</param>
    /// <param name="Why">The sentence that says why it is refused and what to do instead.</param>
    /// <param name="Fragment">Where.</param>
    internal sealed record SqlRefusal(string What, string Why, TSqlFragment Fragment);

    /// <summary>A name the text declares or assigns.</summary>
    /// <param name="Name">The name, with its <c>@</c>.</param>
    /// <param name="Declared">True for a declaration, false for an assignment.</param>
    /// <param name="Fragment">Where.</param>
    internal sealed record SqlVariableUse(string Name, bool Declared, TSqlFragment Fragment);

    /// <summary>A <c>THROW</c> or a <c>RAISERROR</c>.</summary>
    /// <param name="IsThrow">True for <c>THROW</c>, false for <c>RAISERROR</c>.</param>
    /// <param name="Value">The thrown number, or the raised severity; null for a bare rethrow.</param>
    /// <param name="Fragment">Where.</param>
    /// <remarks>The legacy <c>RAISERROR number 'message'</c> form does not parse in the current grammar, so it never gets here.</remarks>
    internal sealed record SqlRaise(bool IsThrow, ScalarExpression? Value, TSqlFragment Fragment);

    /// <summary>
    ///     Parses one raw statement's text with ScriptDom and collects the facts the data-access rules
    ///     judge: the tables it writes, the procedures it executes, the statements it may not hold, the
    ///     names it declares or assigns, and the errors it raises.
    /// </summary>
    internal sealed class SqlInspector : TSqlFragmentVisitor
    {
        /// <summary>The two system procedures distribution text may execute.</summary>
        private static readonly HashSet<string> AllowedProcedures = new(StringComparer.OrdinalIgnoreCase) { "sp_sequence_get_range", "sp_getapplock" };

        /// <summary>The common table expressions of the statement being visited, by name.</summary>
        private Dictionary<string, CommonTableExpression> _ctes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Visits a parsed script; <see cref="Inspect" /> is the entry point.</summary>
        private SqlInspector()
        {
        }

        /// <summary>The parse errors, empty when the text parsed.</summary>
        public IReadOnlyList<ParseError> Errors { get; private set; } = [];

        /// <summary>The number of batches, separated by <c>GO</c>.</summary>
        public int BatchCount { get; private set; }

        /// <summary>The tokens of the parsed text.</summary>
        public IList<TSqlParserToken> Tokens { get; private set; } = [];

        /// <summary>The tables written.</summary>
        public List<SqlTarget> Targets { get; } = [];

        /// <summary>The procedures executed, and the dynamic executions.</summary>
        public List<SqlExecution> Executions { get; } = [];

        /// <summary>The statements refused wherever they appear.</summary>
        public List<SqlRefusal> Refusals { get; } = [];

        /// <summary>The names declared or assigned.</summary>
        public List<SqlVariableUse> Variables { get; } = [];

        /// <summary>The errors raised.</summary>
        public List<SqlRaise> Raises { get; } = [];

        /// <summary>Parses a statement's text and collects its facts.</summary>
        /// <param name="text">The text, with a stand-in for each hole.</param>
        /// <returns>The facts; only <see cref="Errors" /> when the text does not parse.</returns>
        public static SqlInspector Inspect(string text)
        {
            SqlInspector inspector = new();
            TSql170Parser parser = new(initialQuotedIdentifiers: true);
            using StringReader reader = new(text);
            TSqlFragment fragment = parser.Parse(reader, out IList<ParseError> errors);
            if (errors.Count > 0)
            {
                inspector.Errors = [.. errors];
                return inspector;
            }

            inspector.Tokens = fragment.ScriptTokenStream;
            inspector.BatchCount = fragment is TSqlScript script ? script.Batches.Count : 1;
            fragment.Accept(inspector);
            return inspector;
        }

        // ------------------------------------------------------------------
        // Written tables
        // ------------------------------------------------------------------

        /// <summary>Takes the common table expressions of each statement, which its targets may name.</summary>
        /// <param name="node">The statement.</param>
        public override void Visit(StatementWithCtesAndXmlNamespaces node)
        {
            _ctes = new Dictionary<string, CommonTableExpression>(StringComparer.OrdinalIgnoreCase);
            foreach (CommonTableExpression cte in node.WithCtesAndXmlNamespaces?.CommonTableExpressions ?? [])
            {
                _ctes[cte.ExpressionName.Value] = cte;
            }
        }

        /// <summary>Records an <c>INSERT</c>'s target.</summary>
        /// <param name="node">The statement's specification.</param>
        public override void Visit(InsertSpecification node)
        {
            AddTarget("INSERT", node.Target, from: null);
        }

        /// <summary>Records an <c>UPDATE</c>'s target, through its <c>FROM</c> aliases.</summary>
        /// <param name="node">The statement's specification.</param>
        public override void Visit(UpdateSpecification node)
        {
            AddTarget("UPDATE", node.Target, node.FromClause);
        }

        /// <summary>Records a <c>DELETE</c>'s target, through its <c>FROM</c> aliases.</summary>
        /// <param name="node">The statement's specification.</param>
        public override void Visit(DeleteSpecification node)
        {
            AddTarget("DELETE", node.Target, node.FromClause);
        }

        /// <summary>Records the table an <c>OUTPUT ... INTO</c> writes.</summary>
        /// <param name="node">The clause.</param>
        public override void Visit(OutputIntoClause node)
        {
            AddTarget("OUTPUT INTO", node.IntoTable, from: null);
        }

        /// <summary>Records the table a <c>SELECT ... INTO</c> creates.</summary>
        /// <param name="node">The statement.</param>
        public override void Visit(SelectStatement node)
        {
            if (node.Into is SchemaObjectName into)
            {
                Targets.Add(Named("SELECT INTO", into, into));
            }
        }

        /// <summary>Records the table a <c>TRUNCATE TABLE</c> empties.</summary>
        /// <param name="node">The statement.</param>
        public override void Visit(TruncateTableStatement node)
        {
            Targets.Add(Named("TRUNCATE TABLE", node.TableName, node.TableName));
        }

        /// <summary>Refuses <c>MERGE</c>, whose concurrency and trigger semantics the data layer does not take on.</summary>
        /// <param name="node">The statement.</param>
        public override void Visit(MergeStatement node)
        {
            Refusals.Add(new SqlRefusal(
                "MERGE",
                "Write with INSERT, UPDATE and DELETE statements instead; MERGE is never used on the platform's tables.",
                node));
        }

        // ------------------------------------------------------------------
        // Executions
        // ------------------------------------------------------------------

        /// <summary>Records one execution: a procedure, a procedure variable, or a string.</summary>
        /// <param name="node">The execution.</param>
        public override void Visit(ExecuteSpecification node)
        {
            // --- What runs ---
            switch (node.ExecutableEntity)
            {
                case ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name: SchemaObjectName name, AdHocDataSource: null } when node.LinkedServer is null:
                    bool allowed = name.ServerIdentifier is null
                        && name.DatabaseIdentifier is null
                        && (name.SchemaIdentifier is null || string.Equals(name.SchemaIdentifier.Value, "sys", StringComparison.OrdinalIgnoreCase))
                        && AllowedProcedures.Contains(name.BaseIdentifier.Value);
                    Executions.Add(new SqlExecution(Dotted(name), allowed, node));
                    break;

                default:
                    Executions.Add(new SqlExecution(Name: null, IsAllowed: false, node));
                    break;
            }

            // --- What it assigns: the return value and the output parameters ---
            if (node.Variable is VariableReference result)
            {
                Variables.Add(new SqlVariableUse(result.Name, Declared: false, result));
            }

            foreach (ExecuteParameter parameter in node.ExecutableEntity?.Parameters ?? [])
            {
                if (parameter.IsOutput && parameter.ParameterValue is VariableReference output)
                {
                    Variables.Add(new SqlVariableUse(output.Name, Declared: false, output));
                }
            }
        }

        /// <summary>Refuses <c>EXECUTE AS</c>, which would run the rest of the batch under another principal.</summary>
        /// <param name="node">The statement.</param>
        public override void Visit(ExecuteAsStatement node)
        {
            Refusals.Add(new SqlRefusal(
                "EXECUTE AS",
                "The batch runs under the tenant's application principal; a statement never changes it.",
                node));
        }

        // ------------------------------------------------------------------
        // Transactions and session options
        // ------------------------------------------------------------------

        /// <summary>Refuses an isolation level, which the executor alone sets.</summary>
        /// <param name="node">The statement.</param>
        public override void Visit(SetTransactionIsolationLevelStatement node)
        {
            Refusals.Add(new SqlRefusal(
                "SET TRANSACTION ISOLATION LEVEL",
                "The executor alone sets the isolation level; a rule that needs locking reads takes an application lock with sp_getapplock or uses table hints.",
                node));
        }

        /// <summary>Refuses a transaction statement, since the executor alone opens and ends the transaction.</summary>
        /// <param name="node">The statement.</param>
        public override void Visit(TransactionStatement node)
        {
            string what = node switch
            {
                BeginTransactionStatement => "BEGIN TRANSACTION",
                CommitTransactionStatement => "COMMIT",
                RollbackTransactionStatement => "ROLLBACK",
                SaveTransactionStatement => "SAVE TRANSACTION",
                _ => "A transaction statement",
            };
            Refusals.Add(new SqlRefusal(
                what,
                "The executor alone opens, commits and rolls back the batch's transaction; a statement that must fail the batch throws instead.",
                node));
        }

        /// <summary>Refuses the session options that change how the executor's transaction behaves.</summary>
        /// <param name="node">The statement.</param>
        public override void Visit(PredicateSetStatement node)
        {
            if ((node.Options & SetOptions.XactAbort) != 0)
            {
                Refusals.Add(new SqlRefusal(
                    "SET XACT_ABORT",
                    "The executor sets XACT_ABORT for the whole batch; a statement never changes it.",
                    node));
            }

            if ((node.Options & SetOptions.ImplicitTransactions) != 0)
            {
                Refusals.Add(new SqlRefusal(
                    "SET IMPLICIT_TRANSACTIONS",
                    "The executor alone opens the batch's transaction; a statement never changes how transactions start.",
                    node));
            }
        }

        // ------------------------------------------------------------------
        // Declared and assigned names
        // ------------------------------------------------------------------

        /// <summary>Records a declared scalar or cursor variable.</summary>
        /// <param name="node">The declaration.</param>
        public override void Visit(DeclareVariableElement node)
        {
            Variables.Add(new SqlVariableUse(node.VariableName.Value, Declared: true, node));
        }

        /// <summary>Records a declared table variable.</summary>
        /// <param name="node">The declaration.</param>
        public override void Visit(DeclareTableVariableBody node)
        {
            Variables.Add(new SqlVariableUse(node.VariableName.Value, Declared: true, node));
        }

        /// <summary>Records a variable assigned with <c>SET</c>.</summary>
        /// <param name="node">The statement.</param>
        public override void Visit(SetVariableStatement node)
        {
            Variables.Add(new SqlVariableUse(node.Variable.Name, Declared: false, node));
        }

        /// <summary>Records a variable assigned by a <c>SELECT</c>.</summary>
        /// <param name="node">The assignment.</param>
        public override void Visit(SelectSetVariable node)
        {
            Variables.Add(new SqlVariableUse(node.Variable.Name, Declared: false, node));
        }

        /// <summary>Records a variable assigned in an <c>UPDATE</c>'s <c>SET</c>.</summary>
        /// <param name="node">The assignment.</param>
        public override void Visit(AssignmentSetClause node)
        {
            if (node.Variable is VariableReference variable)
            {
                Variables.Add(new SqlVariableUse(variable.Name, Declared: false, node));
            }
        }

        /// <summary>Records the variables a <c>FETCH ... INTO</c> assigns.</summary>
        /// <param name="node">The statement.</param>
        public override void Visit(FetchCursorStatement node)
        {
            foreach (VariableReference variable in node.IntoVariables ?? [])
            {
                Variables.Add(new SqlVariableUse(variable.Name, Declared: false, variable));
            }
        }

        // ------------------------------------------------------------------
        // Raised errors
        // ------------------------------------------------------------------

        /// <summary>Records a <c>THROW</c> and its number.</summary>
        /// <param name="node">The statement.</param>
        public override void Visit(ThrowStatement node)
        {
            Raises.Add(new SqlRaise(IsThrow: true, node.ErrorNumber, node));
        }

        /// <summary>Records a <c>RAISERROR</c> and its severity.</summary>
        /// <param name="node">The statement.</param>
        public override void Visit(RaiseErrorStatement node)
        {
            Raises.Add(new SqlRaise(IsThrow: false, node.SecondParameter, node));
        }

        // ------------------------------------------------------------------
        // Target resolution
        // ------------------------------------------------------------------

        /// <summary>The dotted name as written, without brackets.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The text.</returns>
        private static string Dotted(SchemaObjectName name)
        {
            return string.Join(".", name.Identifiers.Select(static identifier => identifier.Value));
        }

        /// <summary>Classifies a table named by a schema-object name.</summary>
        /// <param name="statement">The statement keyword.</param>
        /// <param name="name">The name.</param>
        /// <param name="fragment">Where.</param>
        /// <returns>The target.</returns>
        private static SqlTarget Named(string statement, SchemaObjectName name, TSqlFragment fragment)
        {
            string table = name.BaseIdentifier.Value;
            if (table.StartsWith("#", StringComparison.Ordinal))
            {
                return new SqlTarget(statement, TargetKind.Local, null, null, null, fragment);
            }

            // An identifier hole whose name the code does not show stands in as [__tellma_holeN].
            return name.SchemaIdentifier is null && SqlHole.IndexOf(table) >= 0
                ? new SqlTarget(statement, TargetKind.UnknownHole, null, null, null, fragment)
                : new SqlTarget(statement, TargetKind.Permanent, name.SchemaIdentifier?.Value ?? "dbo", table, null, fragment);
        }

        /// <summary>Records the table a statement's target names, following an alias or a common table expression.</summary>
        /// <param name="statement">The statement keyword.</param>
        /// <param name="target">The target as written.</param>
        /// <param name="from">The statement's <c>FROM</c>, whose aliases the target may name.</param>
        private void AddTarget(string statement, TableReference? target, FromClause? from)
        {
            if (target is not null)
            {
                // The target is reported where the statement names it, even when an alias or an expression leads elsewhere.
                Targets.Add(Resolve(statement, target, from, depth: 0) with { Fragment = target });
            }
        }

        /// <summary>Resolves one table reference to the table it writes.</summary>
        /// <param name="statement">The statement keyword.</param>
        /// <param name="reference">The reference.</param>
        /// <param name="from">The <c>FROM</c> whose aliases it may name, or null.</param>
        /// <param name="depth">How many aliases and expressions were followed already.</param>
        /// <returns>The target.</returns>
        private SqlTarget Resolve(string statement, TableReference reference, FromClause? from, int depth)
        {
            switch (reference)
            {
                // --- A table variable, a table-valued parameter, or a variable hole ---
                case VariableTableReference variable:
                    return new SqlTarget(statement, TargetKind.Local, null, null, variable.Variable.Name, reference);

                // --- A name: an alias of the FROM, a common table expression, or a table ---
                case NamedTableReference { SchemaObject: SchemaObjectName name } named:
                    if (depth < 8 && name.SchemaIdentifier is null && name.DatabaseIdentifier is null)
                    {
                        string bare = name.BaseIdentifier.Value;
                        if (from is not null && FindAlias(from.TableReferences, bare) is TableReference aliased && !ReferenceEquals(aliased, named))
                        {
                            return Resolve(statement, aliased, from: null, depth + 1);
                        }

                        if (_ctes.TryGetValue(bare, out CommonTableExpression? cte))
                        {
                            return ResolveQuery(statement, cte.QueryExpression, reference, depth + 1);
                        }
                    }

                    return Named(statement, name, reference);

                // --- A derived table, reduced to the one table it reads ---
                case QueryDerivedTable derived:
                    return ResolveQuery(statement, derived.QueryExpression, reference, depth + 1);

                default:
                    return new SqlTarget(statement, TargetKind.Unresolved, null, null, null, reference);
            }
        }

        /// <summary>Resolves a query a target names to the one table it reads from, when it reads from one.</summary>
        /// <param name="statement">The statement keyword.</param>
        /// <param name="query">The query.</param>
        /// <param name="fragment">Where the target is named.</param>
        /// <param name="depth">How many aliases and expressions were followed already.</param>
        /// <returns>The target.</returns>
        private SqlTarget ResolveQuery(string statement, QueryExpression query, TSqlFragment fragment, int depth)
        {
            while (query is QueryParenthesisExpression parenthesis)
            {
                query = parenthesis.QueryExpression;
            }

            return depth < 8 && query is QuerySpecification { FromClause.TableReferences: { Count: 1 } references } && references[0] is not JoinTableReference
                ? Resolve(statement, references[0], from: null, depth)
                : new SqlTarget(statement, TargetKind.Unresolved, null, null, null, fragment);
        }

        /// <summary>Finds the table reference a <c>FROM</c> gives an alias, joins and parentheses included.</summary>
        /// <param name="references">The references.</param>
        /// <param name="alias">The alias.</param>
        /// <returns>The reference, or null.</returns>
        private static TableReference? FindAlias(IEnumerable<TableReference> references, string alias)
        {
            foreach (TableReference reference in references)
            {
                TableReference? found = reference switch
                {
                    TableReferenceWithAlias { Alias.Value: string name } when string.Equals(name, alias, StringComparison.OrdinalIgnoreCase) => reference,

                    // A table without an alias is exposed by its own name, so UPDATE Jobs ... FROM core.Jobs writes core.Jobs.
                    NamedTableReference { Alias: null, SchemaObject: { SchemaIdentifier: not null, BaseIdentifier.Value: string name } }
                        when string.Equals(name, alias, StringComparison.OrdinalIgnoreCase) => reference,
                    JoinTableReference join => FindAlias([join.FirstTableReference, join.SecondTableReference], alias),
                    JoinParenthesisTableReference parenthesis => FindAlias([parenthesis.Join], alias),
                    _ => null,
                };
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
