// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     Model-level fluent configuration of the table-types extension.
    /// </summary>
    public static class TableTypesModelBuilderExtensions
    {
        /// <summary>
        ///     Opts the model into the library's built-in primitive table types for bulk delete /
        ///     bulk lookup scenarios: <c>[IdList]</c> (<c>int</c>), <c>[BigIdList]</c> (<c>bigint</c>),
        ///     <c>[GuidList]</c> (<c>uniqueidentifier</c>) and <c>[StringList]</c> (<c>nvarchar(450)</c>),
        ///     each with a single <c>[Id]</c> primary-key column. They flow through the same
        ///     annotations, differ, operations and SQL as table-derived types.
        /// </summary>
        /// <param name="modelBuilder">The model builder.</param>
        /// <param name="types">Which built-in types to create.</param>
        /// <param name="schema">The schema to create the types in; defaults to <c>dbo</c>.</param>
        /// <param name="grants">
        ///     Database principals that receive <c>GRANT EXECUTE ON TYPE</c> on each built-in type
        ///     after every create/recreate.
        /// </param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public static ModelBuilder HasBuiltInTableTypes(
            this ModelBuilder modelBuilder,
            BuiltInTableTypes types = BuiltInTableTypes.All,
            string schema = "dbo",
            params string[] grants)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            ArgumentException.ThrowIfNullOrEmpty(schema);
            ArgumentNullException.ThrowIfNull(grants);

            BuiltInTableTypesConfiguration configuration = new()
            {
                Types = types,
                Schema = schema,
                Grants = grants,
            };
            modelBuilder.HasAnnotation(TableTypeAnnotationNames.BuiltIn, TableTypeJson.Serialize(configuration));
            return modelBuilder;
        }
    }
}
