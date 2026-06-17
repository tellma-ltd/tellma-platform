// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// SqlServerAnnotationCodeGenerator lives in an ".Internal" namespace. Deriving it is the only
// way to keep the SQL Server provider's annotation rendering while filtering ours, and this
// project exists precisely to absorb design-time coupling (it never ships in a runtime host).
// The coupling surface — base type, constructor, and the FilterIgnoredAnnotations override — is
// compiler-enforced (any EF move is a build break here), and DesignInternalsPinningTests pins the
// base type for a clear diagnostic. Unlike the runtime differ (Rule 1's hard quarantine), a
// design-time-only subclass needs no separate adapter indirection.
#pragma warning disable EF1001 // Internal EF Core API usage.

using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.SqlServer.Design.Internal;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.EntityFrameworkCore.Design
{
    /// <summary>
    ///     The SQL Server annotation code generator, additionally excluding two groups of
    ///     table-type annotations from generic <c>HasAnnotation</c> output:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 <b>Definitions</b> — rendered as readable <c>HasTableTypeDefinition(...)</c>
    ///                 fluent calls by <see cref="TableTypesCSharpSnapshotGenerator" /> instead. The
    ///                 two services are registered together by
    ///                 <see cref="TableTypesDesignTimeServices" />, so a definition is never
    ///                 silently dropped from a snapshot.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 <b>Standalone configurations</b> — the raw registration input the finalizing
    ///                 convention derives definitions from. Snapshots carry the contract (the
    ///                 definitions); the raw input is live-model-only — nothing reads it on the
    ///                 snapshot side (conventions never run there), and its JSON embeds versioned
    ///                 assembly-qualified CLR type names that would churn snapshots on retargeting.
    ///             </description>
    ///         </item>
    ///     </list>
    /// </summary>
    /// <param name="dependencies">The dependencies; pass through to the provider's generator.</param>
    public class TableTypesSqlServerAnnotationCodeGenerator(AnnotationCodeGeneratorDependencies dependencies)
        : SqlServerAnnotationCodeGenerator(dependencies)
    {
        /// <inheritdoc />
        public override IEnumerable<IAnnotation> FilterIgnoredAnnotations(IEnumerable<IAnnotation> annotations)
        {
            return base.FilterIgnoredAnnotations(annotations)
                .Where(a => !a.Name.StartsWith(TableTypeAnnotationNames.DefinitionPrefix, StringComparison.Ordinal)
                    && !a.Name.StartsWith(TableTypeAnnotationNames.StandalonePrefix, StringComparison.Ordinal)
                    && a.Name != TableTypeAnnotationNames.ExcludedKeys);
        }
    }
}
