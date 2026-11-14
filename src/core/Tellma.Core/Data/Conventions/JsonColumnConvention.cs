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
    ///     Maps every <see cref="JsonColumnAttribute" /> string to <c>nvarchar(max)</c>: opaque text the
    ///     data layer binds as text. Never the native <c>json</c> type, which the pinned compatibility
    ///     level keeps from replacing it behind the model's back.
    /// </summary>
    internal sealed class JsonColumnConvention : IModelFinalizingConvention
    {
        /// <summary>The store type of every JSON column.</summary>
        public const string StoreType = "nvarchar(max)";

        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                foreach (IConventionProperty property in entityType.GetDeclaredProperties())
                {
                    if (property.ClrType == typeof(string)
                        && property.PropertyInfo is not null
                        && EntityShapes.GetAttribute<JsonColumnAttribute>(property.PropertyInfo) is not null)
                    {
                        _ = property.Builder.HasColumnType(StoreType, fromDataAnnotation: true);
                    }
                }
            }
        }
    }
}
