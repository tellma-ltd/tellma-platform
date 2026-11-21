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
    ///     Gives the twins <c>P2</c> and <c>P3</c> of every <see cref="MultilingualAttribute" /> anchor
    ///     <c>P</c> the anchor's maximum length and unicode, and makes them nullable: a tenant with fewer
    ///     content languages leaves them empty.
    /// </summary>
    /// <remarks>
    ///     A twin that declares its own length or unicode keeps it, so a mismatch stays visible to the
    ///     metadata validation, which refuses it; a missing twin is likewise left to the validation.
    /// </remarks>
    internal sealed class MultilingualConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                foreach (IConventionProperty anchor in entityType.GetProperties().ToList())
                {
                    if (anchor.ClrType != typeof(string)
                        || anchor.PropertyInfo is null
                        || EntityShapes.GetAttribute<MultilingualAttribute>(anchor.PropertyInfo) is null)
                    {
                        continue;
                    }

                    foreach (string suffix in (string[])[MultilingualAttribute.SecondarySuffix, MultilingualAttribute.TertiarySuffix])
                    {
                        if (entityType.FindProperty(anchor.Name + suffix) is not IConventionProperty twin || twin.ClrType != typeof(string))
                        {
                            continue;
                        }

                        if (!IsDeclared(twin.GetMaxLengthConfigurationSource()))
                        {
                            _ = twin.Builder.HasMaxLength(anchor.GetMaxLength(), fromDataAnnotation: false);
                        }

                        if (!IsDeclared(twin.GetIsUnicodeConfigurationSource()))
                        {
                            _ = twin.Builder.IsUnicode(anchor.IsUnicode(), fromDataAnnotation: false);
                        }

                        _ = twin.Builder.IsRequired(false, fromDataAnnotation: false);
                    }
                }
            }
        }

        /// <summary>Whether a facet of a twin was declared on it or configured, rather than left to conventions.</summary>
        /// <param name="source">The facet's configuration source.</param>
        /// <returns>True for a data annotation or an explicit configuration.</returns>
        private static bool IsDeclared(ConfigurationSource? source)
        {
            return source is ConfigurationSource.DataAnnotation or ConfigurationSource.Explicit;
        }
    }
}
