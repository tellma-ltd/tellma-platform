// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Data;
using Tellma.Core.Testing.Database;
using Tellma.Distro.Acme.Migrator;
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
