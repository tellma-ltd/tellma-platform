// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Gives the <c>IsActive</c> column of every <see cref="IActivatable" /> table the default 1,
    ///     <c>DF_&lt;Table&gt;_IsActive</c>: every row is created active, and the insert writes the
    ///     column's default rather than the payload's value.
    /// </summary>
    internal sealed class ActivatableConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                if (typeof(IActivatable).IsAssignableFrom(entityType.ClrType)
                    && entityType.FindProperty(nameof(IActivatable.IsActive)) is not null)
                {
                    ConventionModel.SetDefault(entityType, nameof(IActivatable.IsActive), true);
                }
            }
        }
    }
}
