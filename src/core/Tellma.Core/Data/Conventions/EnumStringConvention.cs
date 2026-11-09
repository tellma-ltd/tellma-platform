// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Stores every enum property by member name: a string conversion on each property whose type is
    ///     an enum or a nullable enum. The class is the truth, so a member added is never a migration
    ///     per table.
    /// </summary>
    /// <remarks>
    ///     Applied per property over the finished model rather than as a pre-convention type filter, so
    ///     nullable enums are covered by construction. An explicit conversion in a model configuration
    ///     wins over it.
    /// </remarks>
    internal sealed class EnumStringConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                foreach (IConventionProperty property in entityType.GetDeclaredProperties())
                {
                    if (EnumType(property) is not null)
                    {
                        _ = property.Builder.HasConversion(typeof(string), fromDataAnnotation: true);
                    }
                }
            }
        }

        /// <summary>The enum type of a property of an enum or nullable enum type, or <see langword="null" />.</summary>
        /// <param name="property">The property.</param>
        /// <returns>The enum type, or <see langword="null" />.</returns>
        public static Type? EnumType(IReadOnlyProperty property)
        {
            Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            return type.IsEnum ? type : null;
        }
    }
}
