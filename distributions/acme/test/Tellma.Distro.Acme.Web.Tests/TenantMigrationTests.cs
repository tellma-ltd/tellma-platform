// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;
using Tellma.Core.Data;
using Tellma.Distro.Acme.Migrator;

namespace Tellma.Distro.Acme.Web.Tests
{
    /// <summary>
    ///     Acme's tenant migrations against the model both hosts compose: the snapshot holds no change
    ///     the composed model lacks, there is one initial migration until the first release, and its
    ///     script creates no identity column, no rowversion, no trigger and no <c>MERGE</c>, which the
    ///     platform's write path never relies on.
    /// </summary>
    public sealed partial class TenantMigrationTests
    {
        /// <summary>The command that regenerates the initial migration, for a failure to name.</summary>
        private const string Regenerate =
            "regenerate the InitialTenant migration with dotnet ef (remove it and its snapshot, then run the distribution README's migrations add command with the name InitialTenant)";

        [Fact]
        public void The_migrator_s_snapshot_has_no_pending_change_against_the_composed_model()
        {
            using TellmaDbContext context = new AcmeDesignTimeFactory().CreateDbContext([]);

            Assert.False(context.Database.HasPendingModelChanges(), $"The composed tenant model has changes the migrations do not capture; {Regenerate}.");
        }

        [Fact]
        public void The_tenant_model_has_one_migration_the_initial_one_until_the_first_release()
        {
            using TellmaDbContext context = new AcmeDesignTimeFactory().CreateDbContext([]);

            // Until the first release every model change regenerates the one migration, so no
            // deployment ever holds a history this migration set cannot reproduce.
            string migration = Assert.Single(context.Database.GetMigrations());
            Assert.EndsWith("_InitialTenant", migration, StringComparison.Ordinal);
        }

        [Fact]
        public void The_initial_migration_s_script_creates_no_identity_column_rowversion_trigger_or_merge()
        {
            using TellmaDbContext context = new AcmeDesignTimeFactory().CreateDbContext([]);

            // The script the migrator's history would apply from an empty database; no connection is opened.
            string script = context.GetService<IMigrator>().GenerateScript();

            Assert.Contains("CREATE TABLE [gl].[Centers]", script, StringComparison.Ordinal);
            Assert.DoesNotMatch(Identity(), script);
            Assert.DoesNotMatch(RowVersion(), script);
            Assert.DoesNotMatch(Trigger(), script);
            Assert.DoesNotMatch(Merge(), script);
        }

        /// <summary>
        ///     An identity column's keyword. EF's seeding guard around every insert of seed rows, a
        ///     <c>SET IDENTITY_INSERT</c> that runs only when the table has an identity column, is no
        ///     match: the underscore joins it into one word.
        /// </summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"\bIDENTITY\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex Identity();

        /// <summary>A column of the row-version type, under either of its names.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"\]\s+(rowversion|timestamp)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RowVersion();

        /// <summary>A trigger's creation.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"\b(CREATE|ALTER)\s+(OR\s+ALTER\s+)?TRIGGER\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex Trigger();

        /// <summary>A <c>MERGE</c> statement.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"\bMERGE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex Merge();
    }
}
