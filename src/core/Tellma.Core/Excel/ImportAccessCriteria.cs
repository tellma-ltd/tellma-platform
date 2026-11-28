// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Excel
{
    /// <summary>The imports' bespoke criteria: every member reads and deletes the imports they started.</summary>
    internal sealed class ImportAccessCriteria : IAccessCriteriaProvider
    {
        /// <inheritdoc />
        public string Resource => CoreResources.Import;

        /// <inheritdoc />
        public IReadOnlyList<AccessCriterion> Criteria => ExcelAccessCriteria.SelfScope;
    }
}
