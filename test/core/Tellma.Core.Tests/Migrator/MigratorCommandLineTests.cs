// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Migrator.CommandLine;

namespace Tellma.Core.Tests.Migrator
{
    /// <summary>The migrator's command line: every command's options, and the contradictions refused as usage errors.</summary>
    public class MigratorCommandLineTests
    {
        [Fact]
        public void Migrate_defaults_to_every_tenant_with_the_default_parallelism()
        {
            MigrateCommandLine line = Assert.IsType<MigrateCommandLine>(MigratorCommandLine.Parse(["migrate"]));

            Assert.False(line.CatalogOnly);
            Assert.Empty(line.Tenants);
            Assert.Equal(MigratorCommandLine.DefaultParallelism, line.Parallelism);
        }

        [Fact]
        public void Migrate_reads_repeated_tenants_once_each_and_both_option_spellings()
        {
            MigrateCommandLine line = Assert.IsType<MigrateCommandLine>(
                MigratorCommandLine.Parse(["migrate", "--tenant", "3", "--tenant=1", "--tenant", "3", "--parallelism=8"]));

            Assert.Equal([3, 1], line.Tenants);
            Assert.Equal(8, line.Parallelism);
        }

        [Fact]
        public void Migrate_reads_the_catalog_only_and_all_tenants_flags()
        {
            Assert.True(Assert.IsType<MigrateCommandLine>(MigratorCommandLine.Parse(["migrate", "--catalog-only"])).CatalogOnly);
            Assert.Empty(Assert.IsType<MigrateCommandLine>(MigratorCommandLine.Parse(["migrate", "--all-tenants"])).Tenants);
        }

        [Theory]
        [InlineData("migrate", "--all-tenants", "--tenant", "1")]
        [InlineData("migrate", "--catalog-only", "--tenant", "1")]
        [InlineData("migrate", "--catalog-only", "--all-tenants")]
        [InlineData("migrate", "--tenant", "0")]
        [InlineData("migrate", "--tenant", "-1")]
        [InlineData("migrate", "--tenant", "one")]
        [InlineData("migrate", "--parallelism", "0")]
        [InlineData("migrate", "--tenant")]
        [InlineData("migrate", "--parallelism", "2", "--parallelism", "3")]
        [InlineData("migrate", "--unknown")]
        [InlineData("migrate", "operand")]
        [InlineData("provision")]
        [InlineData("provision", "--name", "Acme")]
        [InlineData("provision", "--category", "Live")]
        [InlineData("provision", "--name", "Acme", "--category", "Gold")]
        [InlineData("provision", "--name", "Acme", "--category", "1")]
        [InlineData("provision", "--tenant", "1", "--server", "sql02", "--database", "acme")]
        [InlineData("provision", "--tenant", "1", "--live-tenant", "2")]
        [InlineData("provision", "--name", "Acme", "--category", "Live", "--server", "sql02")]
        [InlineData("set-state", "--tenant", "1")]
        [InlineData("set-state", "--state", "Active")]
        [InlineData("set-state", "--tenant", "1", "--state", "Dormant")]
        [InlineData("set-state", "--tenant", "1", "--state", "2")]
        [InlineData("status", "--tenant", "1")]
        [InlineData("emit-client-assets")]
        [InlineData("emit-client-assets", "a", "b")]
        [InlineData("upgrade")]
        [InlineData("")]
        public void A_contradictory_or_malformed_command_line_is_a_usage_error(params string[] args)
        {
            _ = Assert.Throws<MigratorUsageException>(() => MigratorCommandLine.Parse(args));
        }

        [Fact]
        public void No_arguments_are_a_usage_error()
        {
            _ = Assert.Throws<MigratorUsageException>(() => MigratorCommandLine.Parse([]));
        }

        [Fact]
        public void Provision_registers_with_a_name_and_a_category_and_resumes_with_a_tenant_alone()
        {
            ProvisionCommandLine registration = Assert.IsType<ProvisionCommandLine>(MigratorCommandLine.Parse(
            [
                "provision", "--name", "Acme Sandbox", "--category", "sandbox", "--live-tenant", "1", "--tenant", "7",
                "--server", "sql02", "--database", "acme-sandbox", "--admin-email", "admin@acme.example",
            ]));
            ProvisionCommandLine resumption = Assert.IsType<ProvisionCommandLine>(MigratorCommandLine.Parse(["provision", "--tenant", "1000"]));

            Assert.Equal(new ProvisionCommandLine(7, "Acme Sandbox", TenantCategory.Sandbox, 1, "sql02", "acme-sandbox", "admin@acme.example", null), registration);
            Assert.Equal(new ProvisionCommandLine(1000, null, null, null, null, null, null, null), resumption);
        }

        [Fact]
        public void Set_state_reads_the_state_by_name_and_an_optional_reason()
        {
            SetStateCommandLine line = Assert.IsType<SetStateCommandLine>(
                MigratorCommandLine.Parse(["set-state", "--tenant", "4", "--state", "readonly", "--reason", "Overdue invoice"]));

            Assert.Equal(new SetStateCommandLine(4, TenantState.ReadOnly, "Overdue invoice"), line);
        }

        [Fact]
        public void Emit_and_status_take_their_operand_and_nothing()
        {
            Assert.Equal(new EmitClientAssetsCommandLine("generated"), MigratorCommandLine.Parse(["emit-client-assets", "generated"]));
            _ = Assert.IsType<StatusCommandLine>(MigratorCommandLine.Parse(["status"]));
        }
    }
}
