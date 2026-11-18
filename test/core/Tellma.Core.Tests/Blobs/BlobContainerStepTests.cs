// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Blobs;
using Tellma.Core.Composition;
using Tellma.Core.Testing.Blobs;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     The blob-container provisioning step: composed by the platform under its fixed name, order
    ///     and version, and creating the tenant's storage idempotently.
    /// </summary>
    public class BlobContainerStepTests
    {
        [Fact]
        public void The_platform_composes_the_step_under_its_fixed_name_order_and_version()
        {
            IServiceCollection services = TestComposition.Compose(static _ => { });
            TellmaCompositionRecord record = services.Select(static descriptor => descriptor.ImplementationInstance).OfType<TellmaCompositionRecord>().Single();

            ProvisioningStepRegistration step = Assert.Single(record.ProvisioningSteps, static step => step.StepType == typeof(BlobContainerStep));
            Assert.Equal(CoreFeature.FeatureName, step.Owner.Name);

            BlobContainerStep created = new();
            Assert.Equal(("core.blob-container", 20, 1), (created.Name, created.Order, created.Version));
        }

        [Fact]
        public async Task Running_the_step_creates_the_tenants_storage_and_running_it_again_changes_nothing()
        {
            InMemoryBlobStore store = new();
            BlobContainerStep step = new();
            await using ServiceProvider services = new ServiceCollection().AddSingleton<IBlobStore>(store).BuildServiceProvider();
            TenantProvisioningContext context = new(
                new TenantDescriptor(7, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Provisioning, null),
                services,
                IsNew: true,
                AdminSubject: null,
                AdminEmail: null);

            Assert.False(store.TenantExists(7));
            await step.RunAsync(context);
            await step.RunAsync(context);

            Assert.True(store.TenantExists(7));
            Assert.Empty(store.Names(7));
        }

        [Fact]
        public void A_composition_without_a_store_still_builds_the_step()
        {
            IServiceCollection services = TestComposition.Compose(static _ => { }, inMemoryBlobs: false);
            using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            using IServiceScope scope = provider.CreateScope();

            Assert.Equal(BlobContainerStep.StepName, scope.ServiceProvider.GetRequiredService<BlobContainerStep>().Name);
        }
    }
}
