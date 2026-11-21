// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Stores every timestamp as <c>datetimeoffset(3)</c>: each <see cref="DateTimeOffset" /> or
    ///     nullable <see cref="DateTimeOffset" /> property takes precision 3, unless a precision or a
    ///     column type is declared or configured for it.
    /// </summary>
    /// <remarks>
    ///     Runs before <see cref="AuditConvention" />, whose two audit timestamps take seven digits at
    ///     the same source and so override it. Standalone table types get the same rule from
    ///     <see cref="TableTypeConvention" />.
    /// </remarks>
    internal sealed class TimestampConvention : IModelFinalizingConvention
    {
        /// <summary>The fractional-second digits of every timestamp but the audit pair.</summary>
        public const int Precision = 3;

        /// <summary>The store type of every timestamp but the audit pair.</summary>
        public const string StoreType = "datetimeoffset(3)";

        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                foreach (IConventionProperty property in entityType.GetDeclaredProperties())
                {
                    if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTimeOffset)
                        && !IsConfigured(property.GetPrecisionConfigurationSource())
                        && !IsConfigured(property.GetColumnTypeConfigurationSource()))
                    {
                        _ = property.Builder.HasPrecision(Precision, fromDataAnnotation: true);
                    }
                }
            }
        }

        /// <summary>Whether a facet was set by a declaration or a model configuration, rather than left to conventions.</summary>
        /// <param name="source">The facet's configuration source.</param>
        /// <returns>True for a data annotation or an explicit configuration.</returns>
        private static bool IsConfigured(ConfigurationSource? source)
        {
            return source is ConfigurationSource.DataAnnotation or ConfigurationSource.Explicit;
        }
    }
}
