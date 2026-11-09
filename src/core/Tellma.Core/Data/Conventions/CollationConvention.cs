// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Gives every entity property declared <see cref="CollationAttribute" /> its column collation,
    ///     which the table's row-image type copies. A standalone table type's columns take theirs when
    ///     <see cref="TableTypeConvention" /> registers the type.
    /// </summary>
    internal sealed class CollationConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                foreach (IConventionProperty property in entityType.GetDeclaredProperties())
                {
                    if (property.PropertyInfo is not null
                        && EntityShapes.GetAttribute<CollationAttribute>(property.PropertyInfo) is { } collation)
                    {
                        _ = property.Builder.UseCollation(collation.Name, fromDataAnnotation: true);
                    }
                }
            }
        }
    }
}
