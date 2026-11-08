// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Records <see cref="TableTypeContributionItem" />s for the platform's data context, which
    ///     maps each row class as a user-defined table type. The same row class contributed twice is
    ///     recorded once, for the feature realised first.
    /// </summary>
    internal sealed class TableTypeContributionRealizer : IContributionRealizer<TableTypeContributionItem>
    {
        /// <inheritdoc />
        public void Realize(TableTypeContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            Type? rowType = item.RowType;
            if (rowType is null || !rowType.IsClass || rowType.IsAbstract || rowType.ContainsGenericParameters)
            {
                string described = rowType is null ? "no row type" : TypeDisplay.Qualified(rowType);
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes the table type {described}, which is not a concrete, closed class.",
                    "Contribute a standalone table type as a concrete, non-generic class: contribution.TableType<TRow>()."));
                return;
            }

            if (!context.Record.TableTypes.Exists(existing => existing.Type == rowType))
            {
                context.Record.TableTypes.Add(new ContributedType(rowType, owner));
            }
        }
    }
}
