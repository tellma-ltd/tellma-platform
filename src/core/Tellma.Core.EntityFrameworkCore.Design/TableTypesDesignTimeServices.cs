// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Tellma.Core.EntityFrameworkCore.Design
{
    /// <summary>
    ///     Registers Tellma's design-time services with EF tooling. Discovered through the
    ///     <c>[assembly: DesignTimeServicesReference]</c> attribute that the
    ///     <c>Tellma.Core.EntityFrameworkCore.Design</c> MSBuild targets inject into the consuming
    ///     migrator project's assembly — EF only scans the startup and migrations assemblies for
    ///     that attribute, never referenced libraries, so referencing the package is sufficient and
    ///     no manual wiring is needed.
    /// </summary>
    /// <remarks>
    ///     Referenced design-time services are applied before EF's defaults, and EF registers its
    ///     defaults with <c>TryAdd</c> semantics — so the registrations below take precedence.
    /// </remarks>
    public class TableTypesDesignTimeServices : IDesignTimeServices
    {
        /// <inheritdoc />
        public virtual void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);

            serviceCollection.AddSingleton<ICSharpMigrationOperationGenerator, TableTypesCSharpMigrationOperationGenerator>();
            serviceCollection.AddSingleton<IMigrationsCodeGenerator, TableTypesCSharpMigrationsGenerator>();
        }
    }
}
