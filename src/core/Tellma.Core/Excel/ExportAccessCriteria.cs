// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Excel
{
    /// <summary>The exports' bespoke criteria: every member reads and deletes the exports they created.</summary>
    internal sealed class ExportAccessCriteria : IAccessCriteriaProvider
    {
        /// <inheritdoc />
        public string Resource => CoreResources.Export;

        /// <inheritdoc />
        public IReadOnlyList<AccessCriterion> Criteria => ExcelAccessCriteria.SelfScope;
    }
}
