// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tellma.Core.EntityFrameworkCore.TableTypes.Conventions
{
    /// <summary>
    ///     Registers the table-types conventions with the model building pipeline. Added to the EF
    ///     internal service provider by <c>UseTableTypes()</c>; plugins run after the provider's
    ///     convention set is built, so the appended finalizing convention runs after all provider
    ///     finalizing conventions.
    /// </summary>
    /// <param name="typeMappingSource">The relational type mapping source of the current provider.</param>
    public class TableTypesConventionSetPlugin(IRelationalTypeMappingSource typeMappingSource) : IConventionSetPlugin
    {
        private readonly IRelationalTypeMappingSource _typeMappingSource = typeMappingSource;

        /// <inheritdoc />
        public virtual ConventionSet ModifyConventions(ConventionSet conventionSet)
        {
            ArgumentNullException.ThrowIfNull(conventionSet);

            conventionSet.ModelFinalizingConventions.Add(new TableTypeFinalizingConvention(_typeMappingSource));
            return conventionSet;
        }
    }
}
