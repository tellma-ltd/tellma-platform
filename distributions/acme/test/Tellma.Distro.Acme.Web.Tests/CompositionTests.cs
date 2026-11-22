// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Data;
using Tellma.Core.Testing.Database;
using Tellma.Distro.Acme.Entities;
using Tellma.Distro.Acme.Migrator;
using Tellma.Distro.Acme.Services;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;

namespace Tellma.Distro.Acme.Web.Tests
{
    /// <summary>
    ///     The composition both hosts share: the web host serves the tenant model the migrator migrates,
    ///     the migrations capture it in the platform's collation, and the client assets are emitted from
    ///     it without a database.
    /// </summary>
    public sealed class CompositionTests
    {
        [Fact]
        public async Task The_web_host_serves_the_runtime_model_the_migrator_migrates()
        {
            // --- The model the web host serves: built by AddTellma in the host Program.cs builds ---
            await using WebApplication app = AcmeWebApplication.Build("Development");
            IModel served = app.Services.GetRequiredService<IModel>();

            // --- The model the migrator migrates: its design-time factory's context ---
            using TellmaDbContext migrator = new AcmeDesignTimeFactory().CreateDbContext([]);

            // --- Compared in full: every entity, property, key, index, annotation and table type ---
            Assert.Equal(
                migrator.Model.ToDebugString(MetadataDebugStringOptions.LongDefault),
                served.ToDebugString(MetadataDebugStringOptions.LongDefault));
        }

        /// <summary>
        ///     The surfaces the client specs rely on: Acme's stacks with their actions and extra, the
        ///     invoice lines' child query surface, and GL's centers served over Acme's leaf.
        /// </summary>
        /// <returns>A task that completes when the registry is read.</returns>
        [Fact]
        public async Task The_acme_and_gl_surfaces_the_client_reads_are_composed_over_acme_s_types()
        {
            await using WebApplication app = AcmeWebApplication.Build("Development");
            IStackRegistry stacks = app.Services.GetRequiredService<IStackRegistry>();

            // --- Each stack over its entity, with Acme's own actions and the invoices' extra ---
            Assert.Equal(typeof(Currency), stacks.Find("acme.Currency")?.EntityType);
            StackDescriptor customers = stacks.Find("acme.Customer")!;
            Assert.Equal(typeof(CustomerService), customers.ServiceType);
            Assert.Contains(customers.Actions.OfType<EntityActionDescriptor>(), static action => action is { Name: CustomerService.SetCreditLimitAction, Action: "SetCreditLimit" } && action.ArgumentsType == typeof(SetCreditLimitArguments));
            StackDescriptor invoices = stacks.Find("acme.SalesInvoice")!;
            Assert.Contains(invoices.Actions.OfType<EntityActionDescriptor>(), static action => action is { Name: SalesInvoiceService.CloseAction, Action: "Close", ArgumentsType: null });
            Assert.Contains(RelatedDocumentsContributor.RelatedDocumentsExtra, invoices.Extras);

            // --- The lines' child surface under the invoices, and the centers over Acme's leaf ---
            Assert.Equal("acme.SalesInvoice", stacks.FindChildQuery("acme.SalesInvoiceLine")?.OwnerResource);
            Assert.Equal(typeof(MyCenter), stacks.Find("gl.Center")?.EntityType);
        }

        [Fact]
        public void The_tenant_migrations_capture_the_model_in_the_platform_s_collation()
        {
            using TellmaDbContext context = new AcmeDesignTimeFactory().CreateDbContext([]);

            CollationPin.Verify(context);
        }

        [Fact]
        public async Task The_client_assets_are_emitted_with_no_database_reachable()
        {
            using AcmeEmit emit = await AcmeEmit.RunAsync(TestContext.Current.CancellationToken);

            Assert.True(File.Exists(Path.Combine(emit.Output, "tellma-metadata.ts")), $"No tellma-metadata.ts in {emit.Output}.");
        }
    }
}
