// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.EntityFrameworkCore.TableTypes.Conventions;
using Tellma.Core.EntityFrameworkCore.TableTypes.Internal;

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     The <see cref="IDbContextOptionsExtension" /> behind <c>UseTableTypes()</c>. Additive on
    ///     top of <c>UseSqlServer()</c> — it never wraps or replaces the provider — and carries no
    ///     user options of its own.
    /// </summary>
    public sealed class TableTypesOptionsExtension : IDbContextOptionsExtension
    {
        /// <summary>Creates a new <see cref="TableTypesOptionsExtension" />.</summary>
        public TableTypesOptionsExtension()
        {
            Info = new ExtensionInfo(this);
        }

        /// <inheritdoc />
        public DbContextOptionsExtensionInfo Info { get; }

        /// <summary>
        ///     Installs the table-types services. <c>Replace</c> keeps the result independent of
        ///     whether this runs before or after the provider's own <c>ApplyServices</c> (extension
        ///     application order is not contractual): running first, the provider's later
        ///     <c>TryAdd</c> is a no-op against our registration; running second, <c>Replace</c>
        ///     swaps the provider's registration out.
        /// </summary>
        /// <param name="services">The EF internal service collection.</param>
        public void ApplyServices(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.Replace(ServiceDescriptor.Scoped<IMigrationsModelDiffer, TableTypesMigrationsModelDiffer>());
            services.Replace(ServiceDescriptor.Scoped<IMigrationsSqlGenerator, TableTypesSqlServerMigrationsSqlGenerator>());
            new EntityFrameworkServicesBuilder(services).TryAdd<IConventionSetPlugin, TableTypesConventionSetPlugin>();
        }

        /// <summary>
        ///     Validates that <c>UseSqlServer()</c> is configured: the extension generates SQL
        ///     Server-specific DDL and derives store types through the SQL Server provider.
        /// </summary>
        /// <param name="options">The context options.</param>
        public void Validate(IDbContextOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (!EfCoreInternals.IsSqlServerConfigured(options))
            {
                throw new InvalidOperationException(
                    "UseTableTypes() requires the SQL Server provider: call UseSqlServer(...) before UseTableTypes(). " +
                    "SQL Server table types (UDTTs) are a SQL Server feature with no provider-neutral equivalent.");
            }
        }

        /// <summary>The options metadata of the extension (no user options, no service-provider impact).</summary>
        private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
        {
            /// <inheritdoc />
            public override bool IsDatabaseProvider => false;

            /// <inheritdoc />
            public override string LogFragment => "using TableTypes ";

            /// <inheritdoc />
            public override int GetServiceProviderHashCode()
            {
                return 0;
            }

            /// <inheritdoc />
            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            {
                return other is ExtensionInfo;
            }

            /// <inheritdoc />
            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            {
                ArgumentNullException.ThrowIfNull(debugInfo);
                debugInfo["Tellma.Core.EntityFrameworkCore:UseTableTypes"] = "1";
            }
        }
    }
}
