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
    ///     Backs every <see cref="UniqueAttribute" /> and <see cref="NaturalKeyAttribute" /> with a unique
    ///     index, <c>UX_&lt;Table&gt;_&lt;Column&gt;</c>, filtered to non-null values when the column is
    ///     nullable. On a child the index is unique within one parent:
    ///     <c>UX_&lt;Table&gt;_&lt;ParentKey&gt;_&lt;Column&gt;</c> over <c>(ParentKey, Column)</c>.
    /// </summary>
    /// <remarks>
    ///     The pipeline maps a duplicate-key error to the property's field error by the index's name,
    ///     so the name is part of the contract.
    /// </remarks>
    internal sealed class UniqueConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                // A child's uniqueness is per parent; a child without one parent key is left to the
                // metadata validation, so its index here is table-wide.
                IConventionProperty? parentKey = EntityShapes.IsChild(entityType.ClrType) ? ChildConvention.ParentKey(entityType) : null;
                string table = ConventionModel.Table(entityType);

                foreach (IConventionProperty property in entityType.GetProperties().ToList())
                {
                    if (property.PropertyInfo is null
                        || (EntityShapes.GetAttribute<UniqueAttribute>(property.PropertyInfo) is null
                            && EntityShapes.GetAttribute<NaturalKeyAttribute>(property.PropertyInfo) is null))
                    {
                        continue;
                    }

                    string column = ConventionModel.Column(property);
                    List<IConventionProperty> properties = parentKey is null || parentKey == property ? [property] : [parentKey, property];
                    string? filter = property.IsColumnNullable() ? ConventionModel.NotNullFilter(column) : null;

                    _ = ConventionModel.AddIndex(
                        entityType, properties, ConstraintNames.UniqueIndex(table, properties.Select(ConventionModel.Column)), unique: true, filter);
                }
            }
        }
    }
}
