// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using Tellma.Core.Composition;
using Tellma.Core.Data;
using Tellma.Core.Migrator;
using Tellma.Core.Testing.Database;
using Tellma.Distro.Acme.Migrator;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;
using Tellma.Identity.Migrations;

namespace Tellma.Distro.Acme.Web.Tests
{
    /// <summary>
    ///     The composition both hosts share: the web host and the migrator build the same tenant model,
    ///     the migrations capture it in the platform's collation, and the client assets are emitted from
    ///     it without a database.
    /// </summary>
    public sealed class CompositionTests
    {
        [Fact]
        public void The_web_host_and_the_migrator_build_byte_identical_models()
        {
            // --- Each host's composition, realised by the same design-time path ---
            using TellmaDbContext migrator = new AcmeDesignTimeFactory().CreateDbContext([]);
            using TellmaDbContext web = new WebCompositionFactory().CreateDbContext([]);

            // --- Compared in full: every entity, property, key, index, annotation and table type ---
            IModel migratorModel = migrator.GetService<IDesignTimeModel>().Model;
            IModel webModel = web.GetService<IDesignTimeModel>().Model;
            Assert.Equal(
                migratorModel.ToDebugString(MetadataDebugStringOptions.LongDefault),
                webModel.ToDebugString(MetadataDebugStringOptions.LongDefault));
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
            string output = Path.Combine(Path.GetTempPath(), "tellma-acme-emit-" + Guid.NewGuid().ToString("N"));
            try
            {
                using StringWriter written = new(CultureInfo.InvariantCulture);
                using StringWriter errors = new(CultureInfo.InvariantCulture);

                // The production configuration the client build runs with, over a catalog nothing answers.
                int exitCode = await TellmaMigrator.RunAsync(
                    ["emit-client-assets", output],
                    AcmeComposition.Slug,
                    AcmeComposition.Compose,
                    TellmaIdentityStore.MigrateAsync,
                    new MigratorRunSettings
                    {
                        MigrationsAssembly = typeof(AcmeDesignTimeFactory).Assembly,
                        Out = written,
                        Error = errors,
                        ContentRootPath = AcmePaths.Web,
                        ConfigureConfiguration = static configuration => configuration.AddInMemoryCollection(
                        [
                            new KeyValuePair<string, string?>("Tellma:Catalog:ConnectionString", "Server=tcp:127.0.0.1,9;Database=unreachable;Integrated Security=True;Connect Timeout=1"),
                        ]),
                    },
                    TestContext.Current.CancellationToken);

                Assert.True(exitCode == 0, $"emit-client-assets exited {exitCode}:\n{written}\n{errors}");
                Assert.True(File.Exists(Path.Combine(output, "tellma-metadata.ts")), $"No tellma-metadata.ts in {output}:\n{written}");
            }
            finally
            {
                if (Directory.Exists(output))
                {
                    Directory.Delete(output, recursive: true);
                }
            }
        }

        /// <summary>The web host's composition, both delegates, realised through the migrator's design-time path.</summary>
        private sealed class WebCompositionFactory : TellmaDesignTimeDbContextFactory<TellmaDbContext>
        {
            /// <inheritdoc />
            protected override string Slug => AcmeComposition.Slug;

            /// <inheritdoc />
            public override void Compose(TellmaBuilder builder)
            {
                AcmeComposition.Compose(builder);
                AcmeWebComposition.Compose(builder);
            }
        }
    }
}
