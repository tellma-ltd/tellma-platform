// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using System.Reflection;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Adds the non-unique index of every <see cref="TableIndexAttribute" />, over its properties'
    ///     columns in the declared order, named <c>IX_&lt;Table&gt;_&lt;Column1&gt;_&lt;Column2&gt;…</c>.
    /// </summary>
    internal sealed class TableIndexConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                // Inherited declarations come first, base-most class first, then the leaf's own.
                foreach (TableIndexAttribute index in entityType.ClrType.GetCustomAttributes<TableIndexAttribute>(inherit: true).Reverse())
                {
                    string declaration = ConventionModel.Declaration("TableIndex", entityType);
                    if (index.Properties.Count == 0)
                    {
                        throw new InvalidOperationException($"{declaration} names no property; name the indexed properties in key order.");
                    }

                    List<IConventionProperty> properties = [];
                    foreach (string name in index.Properties)
                    {
                        properties.Add(entityType.FindProperty(name)
                            ?? throw new InvalidOperationException(
                                $"{declaration} names '{name}', which is not a mapped property of the entity. Name the properties with nameof."));
                    }

                    string table = ConventionModel.Table(entityType);
                    _ = ConventionModel.AddIndex(
                        entityType, properties, ConstraintNames.Index(table, properties.Select(ConventionModel.Column)), unique: false, filter: null);
                }
            }
        }
    }
}
