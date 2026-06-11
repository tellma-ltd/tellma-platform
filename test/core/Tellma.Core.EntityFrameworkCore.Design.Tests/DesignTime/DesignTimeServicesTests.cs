// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Microsoft.EntityFrameworkCore.Design;
using Tellma.Core.EntityFrameworkCore.MigrationsHost;

namespace Tellma.Core.EntityFrameworkCore.Design.Tests.DesignTime
{
    /// <summary>
    ///     The design-time discovery wiring: EF tooling scans only the startup and migrations
    ///     assemblies for <see cref="DesignTimeServicesReferenceAttribute" />, so the Design
    ///     package's MSBuild targets inject the attribute into consuming migrator projects. The
    ///     MigrationsHost asset consumes the targets through the repository's
    ///     Directory.Build.targets (the Phase-1 ProjectReference flow); the package flow is
    ///     covered by the CI pack-and-consume leg.
    /// </summary>
    public class DesignTimeServicesTests
    {
        [Fact]
        public void MigrationsHost_assembly_carries_the_injected_attribute()
        {
            Assembly migrationsHost = typeof(MigrationsHostContext).Assembly;

            DesignTimeServicesReferenceAttribute attribute = Assert.Single(
                migrationsHost.GetCustomAttributes<DesignTimeServicesReferenceAttribute>());

            Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", attribute.ForProvider);

            // EF resolves the reference with Type.GetType(..., throwOnError: true); whitespace in
            // the injected multi-line literal is tolerated by the runtime type name parser.
            var resolved = Type.GetType(attribute.TypeName, throwOnError: false);
            Assert.Equal(typeof(TableTypesDesignTimeServices), resolved);
        }

        [Fact]
        public void Tests_own_assembly_does_not_get_the_attribute()
        {
            // The injection target gates on a ProjectReference to the Design project. This test
            // assembly references Design, so it legitimately receives the attribute; the runtime
            // library itself must NOT carry one (it would never be scanned anyway — that is the
            // whole point of the injection design).
            Assembly runtime = typeof(TableTypes.TableTypesOptionsExtension).Assembly;

            Assert.Empty(runtime.GetCustomAttributes<DesignTimeServicesReferenceAttribute>());
        }
    }
}
