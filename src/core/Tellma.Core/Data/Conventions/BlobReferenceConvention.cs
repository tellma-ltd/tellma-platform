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
    ///     Makes every <see cref="BlobReferenceAttribute" /> column a foreign key to <c>core.Blobs</c>,
    ///     <c>FK_&lt;Table&gt;_&lt;Column&gt;</c> with no cascade, unique where it is not null,
    ///     <c>UX_&lt;Table&gt;_&lt;Column&gt;</c>: a blob has one owner row, so the index is single-column
    ///     on a child too.
    /// </summary>
    internal sealed class BlobReferenceConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                foreach (IConventionProperty property in entityType.GetProperties().ToList())
                {
                    if (property.PropertyInfo is null
                        || EntityShapes.GetAttribute<BlobReferenceAttribute>(property.PropertyInfo) is null)
                    {
                        continue;
                    }

                    // The column's type (int?) is the metadata validation's to report; a foreign key
                    // of another type to core.Blobs could not be built at all.
                    string declaration = ConventionModel.Declaration("BlobReference", entityType, property.Name);
                    if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) != typeof(int))
                    {
                        throw new InvalidOperationException($"{declaration} decorates a property of type {property.ClrType.Name}; a blob reference is an int? column.");
                    }

                    IConventionEntityType blobs = ConventionModel.FindCoreTable(modelBuilder.Metadata, CoreTable.Blobs, declaration);
                    _ = ConventionModel.AddForeignKey(entityType, property, blobs, DeleteBehavior.NoAction, unique: true, declaration);

                    string column = ConventionModel.Column(property);
                    _ = ConventionModel.AddIndex(
                        entityType, [property], ConstraintNames.UniqueIndex(ConventionModel.Table(entityType), [column]), unique: true, ConventionModel.NotNullFilter(column));
                }
            }
        }
    }
}
