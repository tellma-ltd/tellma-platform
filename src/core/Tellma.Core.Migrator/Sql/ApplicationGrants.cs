// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Migrator.Sql
{
    /// <summary>
    ///     The statements that give the application role what the web application needs in a
    ///     database, recomputed from the model after every run, and the ones that create the
    ///     application's own database user in a fresh database.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The role gets data access on every schema the model maps except <c>dbo</c>, the right
    ///         to draw from every sequence, and, in a tenant database, read access to the schema
    ///         fingerprint table the executor's guard reads. In the catalog its migration history is
    ///         denied to it, and in in-proc identity mode it also reads the identity store's history.
    ///         The table types' <c>EXECUTE</c> grants come from their own migrations.
    ///     </para>
    ///     <para>Every statement is idempotent, and every identifier is bracketed with <c>]</c> doubled.</para>
    /// </remarks>
    internal static class ApplicationGrants
    {
        /// <summary>The identity store's schema in the catalog in in-proc identity mode.</summary>
        public const string IdentitySchema = "idsvr";

        /// <summary>The default schema, which the role is never granted wholesale.</summary>
        public const string DefaultSchema = "dbo";

        /// <summary>The history table every model keeps its applied migrations in.</summary>
        public const string HistoryTable = "__EFMigrationsHistory";

        /// <summary>The tenant database's schema fingerprint table, which the executor's guard reads.</summary>
        public const string SchemaTable = "__TellmaSchema";

        /// <summary>Creates the application role when the database lacks it.</summary>
        public static string EnsureRole { get; } =
            $"IF DATABASE_PRINCIPAL_ID({Literal(DatabaseRoles.Application)}) IS NULL CREATE ROLE {Quote(DatabaseRoles.Application)};";

        /// <summary>The grants of a tenant database, from the tenant model.</summary>
        /// <param name="model">The tenant model.</param>
        /// <returns>The statements, in a stable order.</returns>
        public static IReadOnlyList<string> ForTenant(IModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            List<string> statements = [EnsureRole];
            statements.AddRange(SchemaGrants(Schemas(model)));
            statements.AddRange(SequenceGrants(model));
            statements.Add($"GRANT SELECT ON OBJECT::{Quote(DefaultSchema)}.{Quote(SchemaTable)} TO {Quote(DatabaseRoles.Application)};");
            return statements;
        }

        /// <summary>The grants of the catalog, from the catalog model and the identity mode.</summary>
        /// <param name="model">The catalog model.</param>
        /// <param name="inProcIdentity">Whether the identity store lives in the catalog, in its own schema.</param>
        /// <returns>The statements, in a stable order.</returns>
        public static IReadOnlyList<string> ForCatalog(IModel model, bool inProcIdentity)
        {
            ArgumentNullException.ThrowIfNull(model);

            SortedSet<string> schemas = Schemas(model);
            if (inProcIdentity)
            {
                _ = schemas.Add(IdentitySchema);
            }

            List<string> statements = [EnsureRole];
            statements.AddRange(SchemaGrants(schemas));
            statements.AddRange(SequenceGrants(model));

            // The catalog's history stays the migrator's; the identity engine's store check reads its own.
            statements.Add($"DENY INSERT, UPDATE, DELETE ON OBJECT::{Quote(CatalogDbContext.Schema)}.{Quote(CatalogDbContext.HistoryTable)} TO {Quote(DatabaseRoles.Application)};");
            if (inProcIdentity)
            {
                statements.Add($"GRANT SELECT ON OBJECT::{Quote(DefaultSchema)}.{Quote(HistoryTable)} TO {Quote(DatabaseRoles.Application)};");
            }

            return statements;
        }

        /// <summary>
        ///     The statements that create the application's database user in a fresh database and add
        ///     it to the role: from an external provider on Azure SQL Database, for the login of the
        ///     same name on any other engine.
        /// </summary>
        /// <param name="principal">The application principal's name; never the role's.</param>
        /// <param name="engineEdition">The server's engine edition.</param>
        /// <returns>The statements.</returns>
        /// <exception cref="ArgumentException"><paramref name="principal" /> is blank or names the role.</exception>
        public static IReadOnlyList<string> ForPrincipal(string principal, int engineEdition)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(principal);
            if (string.Equals(principal, DatabaseRoles.Application, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The application principal cannot be the application role itself.", nameof(principal));
            }

            string source = engineEdition == DatabaseCreation.AzureSqlDatabaseEngineEdition
                ? "FROM EXTERNAL PROVIDER"
                : $"FOR LOGIN {Quote(principal)}";

            return
            [
                $"IF DATABASE_PRINCIPAL_ID({Literal(principal)}) IS NULL CREATE USER {Quote(principal)} {source};",
                $"ALTER ROLE {Quote(DatabaseRoles.Application)} ADD MEMBER {Quote(principal)};",
            ];
        }

        /// <summary>A bracketed identifier, with every <c>]</c> doubled.</summary>
        /// <param name="identifier">The identifier.</param>
        /// <returns>The quoted identifier.</returns>
        public static string Quote(string identifier)
        {
            ArgumentNullException.ThrowIfNull(identifier);
            return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
        }

        /// <summary>A Unicode string literal, with every <c>'</c> doubled.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The literal.</returns>
        public static string Literal(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        /// <summary>Every schema the model's tables and sequences live in, except <c>dbo</c>, in ordinal order.</summary>
        /// <param name="model">The model.</param>
        /// <returns>The schemas.</returns>
        private static SortedSet<string> Schemas(IModel model)
        {
            IEnumerable<string?> tables = model.GetRelationalModel().Tables.Select(static table => table.Schema);
            IEnumerable<string?> sequences = model.GetSequences().Select(static sequence => sequence.Schema);
            return new SortedSet<string>(
                tables.Concat(sequences)
                    .Select(static schema => schema ?? DefaultSchema)
                    .Where(static schema => !string.Equals(schema, DefaultSchema, StringComparison.OrdinalIgnoreCase)),
                StringComparer.Ordinal);
        }

        /// <summary>Data access on each schema.</summary>
        /// <param name="schemas">The schemas.</param>
        /// <returns>One statement per schema.</returns>
        private static IEnumerable<string> SchemaGrants(IEnumerable<string> schemas)
        {
            return schemas.Select(static schema => $"GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::{Quote(schema)} TO {Quote(DatabaseRoles.Application)};");
        }

        /// <summary>The right to draw from each of the model's sequences, in ordinal order of schema and name.</summary>
        /// <param name="model">The model.</param>
        /// <returns>One statement per sequence.</returns>
        private static IEnumerable<string> SequenceGrants(IModel model)
        {
            return model.GetSequences()
                .Select(static sequence => (Schema: sequence.Schema ?? DefaultSchema, sequence.Name))
                .Distinct()
                .OrderBy(static sequence => sequence.Schema, StringComparer.Ordinal)
                .ThenBy(static sequence => sequence.Name, StringComparer.Ordinal)
                .Select(static sequence => $"GRANT UPDATE ON OBJECT::{Quote(sequence.Schema)}.{Quote(sequence.Name)} TO {Quote(DatabaseRoles.Application)};");
        }
    }
}
