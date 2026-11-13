// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.TableTypes;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     A statement being composed through the batch's internal channel: it owns the statement's
    ///     ordinal and names its batch-local parameters and tables after it, until the batch appends
    ///     or discards it.
    /// </summary>
    internal sealed class StatementDraft
    {
        /// <summary>The generic <see cref="Table{TRow}" />, closed per row type for a list a raw statement's hole holds.</summary>
        private static readonly MethodInfo TableMethod = typeof(StatementDraft).GetMethods().Single(static method => method.Name == nameof(Table) && method.IsGenericMethodDefinition);

        /// <summary>The closed <see cref="Table{TRow}" /> methods, by row type.</summary>
        private static readonly ConcurrentDictionary<Type, MethodInfo> TableMethods = new();

        /// <summary>The binder table-valued parameters stream their rows through.</summary>
        private readonly TableTypeBinder _binder;

        /// <summary>The statement's parameters, in the order bound.</summary>
        private readonly List<BatchParameter> _parameters = [];

        /// <summary>The batch-local names the statement declares, which later statements may name.</summary>
        private readonly List<string> _declared = [];

        /// <summary>How many scalar parameters are bound.</summary>
        private int _scalars;

        /// <summary>How many table-valued parameters are bound.</summary>
        private int _tables;

        /// <summary>Creates the draft.</summary>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <param name="section">The frame section it is emitted in.</param>
        /// <param name="binder">The table-type binder.</param>
        public StatementDraft(int ordinal, BatchSection section, TableTypeBinder binder)
        {
            ArgumentNullException.ThrowIfNull(binder);
            Ordinal = ordinal;
            Section = section;
            _binder = binder;
        }

        /// <summary>The statement's batch ordinal.</summary>
        public int Ordinal { get; }

        /// <summary>The frame section it is emitted in.</summary>
        public BatchSection Section { get; }

        /// <summary>The parameters bound so far.</summary>
        public IReadOnlyList<BatchParameter> Parameters => _parameters;

        /// <summary>The batch-local names declared so far.</summary>
        public IReadOnlyList<string> Declared => _declared;

        /// <summary>
        ///     The entity a tree statement maintains, which the executor names in a cycle or depth
        ///     error the statement raises; null for any other statement.
        /// </summary>
        public Type? EntityType { get; set; }

        /// <summary>
        ///     A batch-local name in the statement's namespace, <c>@tb{ordinal}_{word}</c>, declared
        ///     for later statements to name.
        /// </summary>
        /// <param name="word">The name within the statement: letters, digits and underscores.</param>
        /// <returns>The name.</returns>
        /// <exception cref="ArgumentException">The word is not a plain identifier.</exception>
        public string Name(string word)
        {
            ArgumentException.ThrowIfNullOrEmpty(word);
            if (!SqlNames.IsPlainWord(word))
            {
                throw new ArgumentException($"The statement-local name '{word}' must be letters, digits and underscores.", nameof(word));
            }

            string name = ReservedSqlNames.Statement(Ordinal, word);
            _declared.Add(name);
            return name;
        }

        /// <summary>Binds a scalar parameter, <c>@tb{ordinal}_p{n}</c>, typed from the value's CLR type.</summary>
        /// <param name="value">The value, or null for SQL <c>NULL</c>.</param>
        /// <returns>The parameter's name.</returns>
        public string Scalar(object? value)
        {
            string name = ReservedSqlNames.Statement(Ordinal, ReservedSqlNames.StatementNames.Scalar(_scalars));
            _parameters.Add(ScalarValues.Describe(name, value));
            _scalars++;
            return name;
        }

        /// <summary>Binds rows of a table type as a table-valued parameter, <c>@tb{ordinal}_t{n}</c>.</summary>
        /// <typeparam name="TRow">The row type: a standalone table type or an entity with a table type.</typeparam>
        /// <param name="rows">The rows, enumerated anew each time the command is sent.</param>
        /// <returns>The parameter's name.</returns>
        /// <exception cref="InvalidOperationException">The row type has no table type.</exception>
        public string Table<TRow>(IReadOnlyList<TRow> rows)
            where TRow : class
        {
            ArgumentNullException.ThrowIfNull(rows);

            // Resolving the binding now refuses a type without a table type at append, not at send.
            TableTypeBinding binding = _binder.Binding(typeof(TRow));
            string name = ReservedSqlNames.Statement(Ordinal, ReservedSqlNames.StatementNames.TableValued(_tables));
            _parameters.Add(new TableParameter(name, rows, rows.Count, parameterName => binding.Parameter(parameterName, rows)));
            _declared.Add(name);
            _tables++;
            return name;
        }

        /// <summary>
        ///     Binds rows of a type known only at run time as a table-valued parameter,
        ///     <c>@tb{ordinal}_t{n}</c>: the rows of an entity with a table type.
        /// </summary>
        /// <param name="rowType">The row type.</param>
        /// <param name="rows">The rows, each of <paramref name="rowType" />, enumerated anew each time the command is sent.</param>
        /// <returns>The parameter's name.</returns>
        /// <exception cref="InvalidOperationException">The row type has no table type.</exception>
        public string Table(Type rowType, IReadOnlyList<object> rows)
        {
            ArgumentNullException.ThrowIfNull(rowType);
            ArgumentNullException.ThrowIfNull(rows);

            TableTypeBinding binding = _binder.Binding(rowType);
            string name = ReservedSqlNames.Statement(Ordinal, ReservedSqlNames.StatementNames.TableValued(_tables));
            _parameters.Add(new TableParameter(name, rows, rows.Count, parameterName => binding.Parameter(parameterName, rows)));
            _declared.Add(name);
            _tables++;
            return name;
        }

        /// <summary>
        ///     Binds a list a raw statement's hole holds as a table-valued parameter when it is a list
        ///     of a table-type row.
        /// </summary>
        /// <param name="value">The hole's value.</param>
        /// <param name="name">The parameter's name, when bound.</param>
        /// <returns>Whether the value is a list of reference-type rows and was bound.</returns>
        public bool TryTable(object value, out string? name)
        {
            ArgumentNullException.ThrowIfNull(value);

            Type? rowType = value.GetType().GetInterfaces()
                .Where(static candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
                .Select(static candidate => candidate.GetGenericArguments()[0])
                .FirstOrDefault(static candidate => !candidate.IsValueType);
            if (rowType is null)
            {
                name = null;
                return false;
            }

            MethodInfo method = TableMethods.GetOrAdd(rowType, static type => TableMethod.MakeGenericMethod(type));
            try
            {
                name = (string)method.Invoke(this, [value])!;
                return true;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                // The binder's own refusal, not the reflection wrapper.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        /// <summary>
        ///     Binds a parameter a query compiled at the statement's ordinal names: an engine name,
        ///     <c>@qx{ordinal}_…</c>.
        /// </summary>
        /// <param name="parameter">The parameter.</param>
        /// <exception cref="ArgumentException">The name is not in the engine namespace of the statement's ordinal.</exception>
        public void Engine(BatchParameter parameter)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            string prefix = string.Create(CultureInfo.InvariantCulture, $"{ReservedSqlNames.EnginePrefix}{Ordinal}_");
            if (!parameter.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"The parameter '{parameter.Name}' is not in the engine namespace of statement {Ordinal} ({prefix}…).", nameof(parameter));
            }

            _parameters.Add(parameter);
        }

        /// <summary>Binds a parameter under a batch-wide reserved name (<c>@tm_…</c>).</summary>
        /// <param name="parameter">The parameter.</param>
        /// <exception cref="ArgumentException">The name is not in the batch-wide reserved namespace.</exception>
        public void Reserved(BatchParameter parameter)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            if (!parameter.Name.StartsWith(ReservedSqlNames.BatchPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"The parameter '{parameter.Name}' is not a batch-wide reserved name ({ReservedSqlNames.BatchPrefix}…).", nameof(parameter));
            }

            _parameters.Add(parameter);
        }
    }
}
