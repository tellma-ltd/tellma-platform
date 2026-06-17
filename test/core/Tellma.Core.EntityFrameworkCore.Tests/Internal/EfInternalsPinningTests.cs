// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The whole point of this file is to pin the internal EF Core surface the adapter relies on.
#pragma warning disable EF1001 // Internal EF Core API usage

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Reflection;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Internal;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.Internal
{
    /// <summary>
    ///     Pins every internal EF Core API the quarantined adapter depends on (spec 0001 Rule 1). When
    ///     an EF upgrade changes this surface, these tests fail loudly and point at the single file
    ///     that needs to change: <c>TableTypes/Internal/EfCoreInternalsAdapter.cs</c>.
    /// </summary>
    public class EfInternalsPinningTests
    {
        /// <summary>The EF Core major version this library is pinned to (see Directory.Packages.props).</summary>
        private const int PinnedEfCoreMajor = 10;

        [Fact]
        public void Ef_core_major_version_is_pinned()
        {
            AssemblyName efCore = typeof(DbContext).Assembly.GetName();

            Assert.Equal(PinnedEfCoreMajor, efCore.Version!.Major);
        }

        [Fact]
        public void MigrationsModelDiffer_constructor_signature_is_unchanged()
        {
            // The adapter's constructor passes through these exact parameter types; a change here
            // means EfCoreInternalsAdapter.cs needs updating.
            Type differType = typeof(TableTypesMigrationsModelDiffer).BaseType!;

            Assert.Equal("Microsoft.EntityFrameworkCore.Migrations.Internal.MigrationsModelDiffer", differType.FullName);

            ConstructorInfo constructor = Assert.Single(differType.GetConstructors());
            string[] parameterTypes = [.. constructor.GetParameters().Select(p => p.ParameterType.FullName!)];

            Assert.Equal(
                [
                    "Microsoft.EntityFrameworkCore.Storage.IRelationalTypeMappingSource",
                    "Microsoft.EntityFrameworkCore.Migrations.IMigrationsAnnotationProvider",
                    "Microsoft.EntityFrameworkCore.Metadata.IRelationalAnnotationProvider",
                    "Microsoft.EntityFrameworkCore.Update.Internal.IRowIdentityMapFactory",
                    "Microsoft.EntityFrameworkCore.Update.Internal.CommandBatchPreparerDependencies",
                ],
                parameterTypes);
        }

        [Fact]
        public void UseTableTypes_replaces_the_differ_and_sql_generator()
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<Plain>());

            Assert.IsType<TableTypesMigrationsModelDiffer>(context.GetService<IMigrationsModelDiffer>());
            Assert.IsType<TableTypesSqlServerMigrationsSqlGenerator>(context.GetService<IMigrationsSqlGenerator>());
        }

        [Fact]
        public void Without_UseTableTypes_the_provider_defaults_remain()
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<Plain>(), useTableTypes: false);

            Assert.IsNotType<TableTypesMigrationsModelDiffer>(context.GetService<IMigrationsModelDiffer>());
            Assert.IsNotType<TableTypesSqlServerMigrationsSqlGenerator>(context.GetService<IMigrationsSqlGenerator>());
        }

        [Fact]
        public void SqlServerOptionsExtension_still_exists_at_the_probed_identity()
        {
            // Validate() type-checks against this internal type to detect UseSqlServer().
            Type? type = typeof(SqlServerDbContextOptionsExtensions).Assembly
                .GetType("Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal.SqlServerOptionsExtension");

            Assert.NotNull(type);
        }

        [Fact]
        public void UseTableTypes_without_UseSqlServer_fails_validation()
        {
            DbContextOptionsBuilder optionsBuilder = new();
            optionsBuilder.UseTableTypes("TestScope").EnableServiceProviderCaching(false);

            // Extension validation runs eagerly in the DbContext constructor.
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            {
                using ModelTestContext context = new(optionsBuilder.Options, mb => mb.Entity<Plain>());
            });
            Assert.Contains("UseSqlServer", exception.Message, StringComparison.Ordinal);
        }
    }
}
