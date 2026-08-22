// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Queryex;

namespace Tellma.Queryex.Testing.Schema
{
    /// <summary>
    ///     The tables the fixture schema describes, as statements a real server will accept.
    /// </summary>
    /// <remarks>
    ///     Derived from the fixture's own descriptors rather than written out beside them, so the
    ///     tables a differential run executes against cannot drift from the schema the compiler was
    ///     given. A column that exists in one and not the other is impossible by construction.
    /// </remarks>
    public static class LedgerDdl
    {
        /// <summary>The collation every text column is created with.</summary>
        /// <remarks>
        ///     Stated rather than inherited, because comparison and ordering of text are exactly what
        ///     a differential run is checking and a server's own default would make the answer depend
        ///     on which server it was.
        /// </remarks>
        public const string Collation = "Latin1_General_100_CI_AS_SC";

        /// <summary>The statements that create the fixture's tables.</summary>
        /// <returns>The script.</returns>
        public static string Create()
        {
            StringBuilder script = new();
            foreach (string name in SchemaNames())
            {
                script.Append("IF SCHEMA_ID('").Append(name).Append("') IS NULL EXEC('CREATE SCHEMA [")
                    .Append(name).Append("]');\n");
            }

            foreach (EntityDescriptor entity in LedgerFixture.Schema.Entities)
            {
                AppendTable(script, entity);
            }

            return script.ToString();
        }

        /// <summary>The statements that remove the fixture's tables.</summary>
        /// <returns>The script.</returns>
        public static string Drop()
        {
            StringBuilder script = new();

            // Dropped in reverse declaration order so a table is gone before whatever pointed at it,
            // and unconditionally, so a half-created fixture still cleans up.
            foreach (EntityDescriptor entity in LedgerFixture.Schema.Entities.Reverse())
            {
                script.Append("DROP TABLE IF EXISTS ").Append(entity.Source).Append(";\n");
            }

            return script.ToString();
        }

        /// <summary>The SQL spelling of one store type.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The type name.</returns>
        public static string TypeName(QueryexStoreType type)
        {
            return type.Family switch
            {
                QueryexStoreFamily.QxBit => "bit",
                QueryexStoreFamily.QxTinyInt => "tinyint",
                QueryexStoreFamily.QxSmallInt => "smallint",
                QueryexStoreFamily.QxInt => "int",
                QueryexStoreFamily.QxBigInt => "bigint",
                QueryexStoreFamily.QxDecimal => Faceted("decimal", type.Size ?? 38, type.Scale ?? 6),
                QueryexStoreFamily.QxChar => Sized("char", type.Size),
                QueryexStoreFamily.QxVarChar => Sized("varchar", type.Size),
                QueryexStoreFamily.QxNChar => Sized("nchar", type.Size),
                QueryexStoreFamily.QxNVarChar => Sized("nvarchar", type.Size),
                QueryexStoreFamily.QxUniqueIdentifier => "uniqueidentifier",
                QueryexStoreFamily.QxDate => "date",
                QueryexStoreFamily.QxDateTime => "datetime",
                QueryexStoreFamily.QxDateTime2 => Sized("datetime2", type.Size ?? 7),
                QueryexStoreFamily.QxDateTimeOffset => Sized("datetimeoffset", type.Size ?? 7),
                QueryexStoreFamily.QxHierarchyId => "hierarchyid",
                QueryexStoreFamily.QxGeography => "geography",
                _ => "nvarchar(4000)",
            };
        }

        /// <summary>Writes one table.</summary>
        /// <param name="script">The script being built.</param>
        /// <param name="entity">The entity.</param>
        private static void AppendTable(StringBuilder script, EntityDescriptor entity)
        {
            script.Append("CREATE TABLE ").Append(entity.Source).Append(" (\n");

            for (int index = 0; index < entity.Properties.Count; index++)
            {
                PropertyDescriptor property = entity.Properties[index];
                script.Append("    [").Append(property.Column).Append("] ")
                    .Append(TypeName(StoreTypeOf(property)));

                if (IsText(StoreTypeOf(property).Family))
                {
                    script.Append(" COLLATE ").Append(Collation);
                }

                script.Append(property.IsNotNull ? " NOT NULL" : " NULL");
                if (index < entity.Properties.Count - 1)
                {
                    script.Append(',');
                }

                script.Append('\n');
            }

            script.Append(");\n");
            script.Append("ALTER TABLE ").Append(entity.Source).Append(" ADD CONSTRAINT [PK_")
                .Append(entity.Name).Append("] PRIMARY KEY ([").Append(entity.Key.Column).Append("]);\n");

            foreach (PropertyDescriptor property in entity.Properties)
            {
                if (property.IsUnique && !ReferenceEquals(property, entity.Key))
                {
                    // Created for real, because the hierarchy lookups depend on the promise and a
                    // fixture that only claimed it would let a broken promise pass unnoticed.
                    script.Append("CREATE UNIQUE INDEX [UQ_").Append(entity.Name).Append('_')
                        .Append(property.Name).Append("] ON ").Append(entity.Source)
                        .Append(" ([").Append(property.Column).Append("]);\n");
                }
            }
        }

        /// <summary>The declared type of a property, or the default for its type in the language.</summary>
        /// <param name="property">The property.</param>
        /// <returns>The store type.</returns>
        private static QueryexStoreType StoreTypeOf(PropertyDescriptor property)
        {
            return property.StoreType ?? property.Type switch
            {
                QueryexType.QxBool => QueryexStoreType.QxBit,
                QueryexType.QxNumeric => QueryexStoreType.QxDecimal(38, 6),
                QueryexType.QxGuid => QueryexStoreType.QxUniqueIdentifier,
                QueryexType.QxDate => QueryexStoreType.QxDate,
                QueryexType.QxDateTime => QueryexStoreType.QxDateTime2(7),
                QueryexType.QxDateTimeOffset => QueryexStoreType.QxDateTimeOffset(7),
                QueryexType.QxHierarchyId => QueryexStoreType.QxHierarchyId,
                QueryexType.QxGeography => QueryexStoreType.QxGeography,
                QueryexType.QxString => QueryexStoreType.QxNVarChar(4000),
                _ => QueryexStoreType.QxNVarChar(4000),
            };
        }

        /// <summary>Whether a family holds text, which is what needs a stated collation.</summary>
        /// <param name="family">The family.</param>
        /// <returns>True when it does.</returns>
        private static bool IsText(QueryexStoreFamily family)
        {
            return family is QueryexStoreFamily.QxChar
                or QueryexStoreFamily.QxVarChar
                or QueryexStoreFamily.QxNChar
                or QueryexStoreFamily.QxNVarChar;
        }

        /// <summary>The schema names the fixture's sources mention, deduplicated.</summary>
        /// <returns>The names.</returns>
        private static IEnumerable<string> SchemaNames()
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (EntityDescriptor entity in LedgerFixture.Schema.Entities)
            {
                int close = entity.Source.IndexOf(']', StringComparison.Ordinal);
                if (entity.Source.StartsWith('[') && close > 1 && seen.Add(entity.Source[1..close]))
                {
                    yield return entity.Source[1..close];
                }
            }
        }

        /// <summary>A type with one facet.</summary>
        /// <param name="name">The type name.</param>
        /// <param name="size">The facet, or null for the unbounded form.</param>
        /// <returns>The rendered type.</returns>
        private static string Sized(string name, int? size)
        {
            return size is int width
                ? name + "(" + width.ToString(CultureInfo.InvariantCulture) + ")"
                : name + "(max)";
        }

        /// <summary>A type with two facets.</summary>
        /// <param name="name">The type name.</param>
        /// <param name="precision">The first facet.</param>
        /// <param name="scale">The second.</param>
        /// <returns>The rendered type.</returns>
        private static string Faceted(string name, int precision, int scale)
        {
            return name
                + "("
                + precision.ToString(CultureInfo.InvariantCulture)
                + ", "
                + scale.ToString(CultureInfo.InvariantCulture)
                + ")";
        }
    }
}
