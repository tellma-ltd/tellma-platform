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
    /// <remarks>
    ///     The attribute is the only way a column of the tenant model declares a collation of its
    ///     own: the runtime model keeps no collation, so what reads one at run time, such as the
    ///     query schema, reads the attribute. A collation configured fluently on a property would be
    ///     invisible to it, so it fails the model.
    /// </remarks>
    internal sealed class CollationConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                foreach (IConventionProperty property in entityType.GetDeclaredProperties())
                {
                    // --- A fluent collation is refused: the attribute is the one source ---
                    if (property.GetCollationConfigurationSource() == ConfigurationSource.Explicit)
                    {
                        throw new InvalidOperationException(
                            $"{entityType.ClrType.Name}.{property.Name} configures its collation with UseCollation. A column of the tenant model declares its own collation with [Collation] on the property, which is what reads the collation at run time.");
                    }

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
