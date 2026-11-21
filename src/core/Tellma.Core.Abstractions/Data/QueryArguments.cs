// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The parameters a query statement declares, each either bound to a value or omitted. An
    ///     omitted parameter has no slot: the compile folds the comparisons that name it out of the
    ///     query, so a filter on an unsupplied parameter matches every row.
    /// </summary>
    /// <remarks>Parameter names are compared without regard to case.</remarks>
    public sealed class QueryArguments
    {
        private readonly List<QueryexParameterDeclaration> _declarations = [];

        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _omitted = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every declared parameter, bound or omitted, in the order declared.</summary>
        public IReadOnlyList<QueryexParameterDeclaration> Declarations => _declarations;

        /// <summary>The bound parameters' values by name; an omitted name is absent.</summary>
        public IReadOnlyDictionary<string, object?> Values => _values;

        /// <summary>The names of the omitted parameters.</summary>
        public IReadOnlyCollection<string> Omitted => _omitted;

        /// <summary>Declares a parameter and binds it to a value.</summary>
        /// <param name="declaration">The parameter's declaration.</param>
        /// <param name="value">
        ///     The value, of the CLR type the declared query type maps to; null binds SQL <c>NULL</c>.
        /// </param>
        /// <returns>These arguments.</returns>
        /// <exception cref="ArgumentException">
        ///     The name is already declared, or the value is null for a parameter declared not null.
        /// </exception>
        public QueryArguments Add(QueryexParameterDeclaration declaration, object? value)
        {
            Declare(declaration);
            if (value is null && declaration.IsNotNull)
            {
                throw new ArgumentException($"The parameter '{declaration.Name}' is declared not null, so it cannot be bound to null.", nameof(value));
            }

            _declarations.Add(declaration);
            _values.Add(declaration.Name, value);
            return this;
        }

        /// <summary>Declares a parameter without a value: it has no slot and the compile folds it out.</summary>
        /// <param name="declaration">The parameter's declaration.</param>
        /// <returns>These arguments.</returns>
        /// <exception cref="ArgumentException">The name is already declared.</exception>
        public QueryArguments Omit(QueryexParameterDeclaration declaration)
        {
            Declare(declaration);
            _declarations.Add(declaration);
            _ = _omitted.Add(declaration.Name);
            return this;
        }

        /// <summary>Refuses a null declaration and a name declared before.</summary>
        /// <param name="declaration">The declaration.</param>
        private void Declare(QueryexParameterDeclaration declaration)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            if (_values.ContainsKey(declaration.Name) || _omitted.Contains(declaration.Name))
            {
                throw new ArgumentException($"The parameter '{declaration.Name}' is already declared.", nameof(declaration));
            }
        }
    }
}
