// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>One constant statement, and what compiling it is supposed to produce.</summary>
    public sealed record ConstantCase
    {
        /// <summary>
        ///     A stable name for this case, which is also the name of its stored snapshot.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>The statement's items, in order.</summary>
        public required IReadOnlyList<ConstantItem> Items { get; init; }

        /// <summary>Whether execution will have a signed-in user.</summary>
        public bool HasUser { get; init; } = true;

        /// <summary>The statement's position among the ones a host executes together.</summary>
        public int BatchOrdinal { get; init; }

        /// <summary>The ceilings, when this case is about one of them.</summary>
        public QueryexLimits Limits { get; init; } = QueryexLimits.Default;

        /// <summary>
        ///     The diagnostic codes the case expects. Empty means the statement is expected to
        ///     compile, in which case its SQL is compared against a stored snapshot.
        /// </summary>
        public IReadOnlyList<string> Diagnostics { get; init; } = [];

        /// <summary>
        ///     The diagnostics the case pins exactly: location, span and arguments as well as code.
        /// </summary>
        /// <remarks>
        ///     Each must be among what the compilation reports, and its code must also be listed in
        ///     <see cref="Diagnostics" />, which stays the complete set of codes the case expects.
        /// </remarks>
        public IReadOnlyList<DiagnosticSite> Sites { get; init; } = [];

        /// <summary>The options the case compiles under.</summary>
        /// <param name="schema">The schema to compile against.</param>
        /// <returns>The options.</returns>
        public ConstantCompilationOptions Options(QueryexSchema schema)
        {
            ArgumentNullException.ThrowIfNull(schema);

            return new ConstantCompilationOptions
            {
                Schema = schema,
                QueryexVersion = QueryexLanguage.Version,
                HasUser = HasUser,
                BatchOrdinal = BatchOrdinal,
                Limits = Limits,
            };
        }

        /// <summary>Returns the case's name, so a failing theory names itself.</summary>
        /// <returns>The identifier.</returns>
        public override string ToString()
        {
            return Id;
        }
    }
}
