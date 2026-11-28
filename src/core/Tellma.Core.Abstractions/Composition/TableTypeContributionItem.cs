// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A standalone table-type class a feature contributes: the row type of a table-valued
    ///     parameter that is not an entity. The platform's data context maps it as a SQL Server
    ///     user-defined table type, so the feature's statements can bind rows of it.
    /// </summary>
    /// <param name="RowType">The row class.</param>
    public sealed record TableTypeContributionItem(Type RowType) : FeatureContributionItem;
}
