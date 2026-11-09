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
    ///     Makes every <see cref="ReferencesAttribute" /> property a many-to-one foreign key to the table
    ///     of the leaf registered for its target, <c>FK_&lt;Table&gt;_&lt;Column&gt;</c> with no cascade,
    ///     indexed by <c>IX_&lt;Table&gt;_&lt;Column&gt;</c>.
    /// </summary>
    internal sealed class ReferenceConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                foreach (IConventionProperty property in entityType.GetProperties().ToList())
                {
                    if (property.PropertyInfo is null
                        || EntityShapes.GetAttribute<ReferencesAttribute>(property.PropertyInfo) is not { } references)
                    {
                        continue;
                    }

                    string declaration = ConventionModel.Declaration("References", entityType, property.Name);
                    Type keyType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                    if (keyType != typeof(int) && keyType != typeof(long))
                    {
                        throw new InvalidOperationException(
                            $"{declaration} decorates a property of type {property.ClrType.Name}; a reference is an int or long foreign key, typed like its target's key.");
                    }

                    IConventionEntityType target = ConventionModel.Leaf(modelBuilder.Metadata, references.Target, declaration);
                    _ = ConventionModel.AddForeignKey(entityType, property, target, DeleteBehavior.NoAction, unique: false, declaration);

                    string table = ConventionModel.Table(entityType);
                    _ = ConventionModel.AddIndex(
                        entityType, [property], ConstraintNames.Index(table, [ConventionModel.Column(property)]), unique: false, filter: null);
                }
            }
        }
    }
}
