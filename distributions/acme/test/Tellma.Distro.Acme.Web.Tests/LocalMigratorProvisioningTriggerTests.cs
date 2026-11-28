// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using Tellma.Distro.Acme.Web.Provisioning;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;

namespace Tellma.Distro.Acme.Web.Tests
{
    /// <summary>
    ///     The Development provisioning trigger's command: the distribution's own Migrator project,
    ///     run from the source checkout in the web host's environment with the tenant's id.
    /// </summary>
    public sealed class LocalMigratorProvisioningTriggerTests
    {
        [Fact]
        public void The_trigger_runs_the_migrator_project_beside_the_web_project_with_the_tenant_s_id()
        {
            ProcessStartInfo start = LocalMigratorProvisioningTrigger.StartInfo(AcmePaths.Web, "Development", 1000);

            string project = Path.Combine(AcmePaths.Migrator, "Tellma.Distro.Acme.Migrator.csproj");
            Assert.Equal("dotnet", start.FileName);
            Assert.Equal(["run", "--project", project, "--no-launch-profile", "--", "provision", "--tenant", "1000"], start.ArgumentList);
            Assert.Equal(AcmePaths.Migrator, start.WorkingDirectory);
            Assert.Equal("Development", start.Environment["DOTNET_ENVIRONMENT"]);
            Assert.False(start.UseShellExecute);
            Assert.True(start.RedirectStandardOutput && start.RedirectStandardError);
        }

        [Fact]
        public void A_web_host_outside_a_source_checkout_is_told_to_provision_with_the_migrator()
        {
            string elsewhere = Path.Combine(Path.GetTempPath(), "tellma-acme-" + Guid.NewGuid().ToString("N"), "web");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => LocalMigratorProvisioningTrigger.StartInfo(elsewhere, "Development", 1000));

            Assert.Contains("provision command", exception.Message, StringComparison.Ordinal);
        }
    }
}
