// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Composition;
using Tellma.Core.Data.Metadata;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     Registers what blobs need beyond the host's store: the options, validated by the startup
    ///     gate; the scratch directory; the upload and attach instruments; the kind registry built from
    ///     the realised model; the scoped staging and download service; and the <c>blobs</c> checks.
    /// </summary>
    internal static class BlobsRuntimeComposition
    {
        /// <summary>Registers the options, the registry, the service and the check.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="record">The realised composition: its configuration, its kind transforms and its built-in checks.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, TellmaCompositionRecord record, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, the scratch directory and the registrations the check reads ---
            options.Bind<BlobOptions>(BlobOptions.SectionPath, CoreFeature.FeatureName);
            services.TryAddSingleton(ScratchDirectory.From(record.Configuration));
            services.AddSingleton(new BlobRegistrations(services));

            // --- The upload and attach instruments, on the host's meter factory ---
            services.AddMetrics();
            services.TryAddSingleton<BlobUploadMetrics>();
            services.TryAddSingleton<BlobAttachMetrics>();

            // --- The kind registry, one per process, over the model's metadata and the stacks ---
            // Factories, so the container's build-time validation leaves the dependencies to the gate.
            services.TryAddSingleton(static provider => new BlobKindRegistry(
                provider.GetRequiredService<EntityMetadataSource>(), provider.GetRequiredService<IStackRegistry>()));
            services.TryAddSingleton<IBlobKindRegistry>(static provider => provider.GetRequiredService<BlobKindRegistry>());

            // --- The staging and download service, per scope: it reads the scope's tenant and user ---
            // Factories too: a host without a store fails the blobs.deployment check, not the container's validation.
            services.TryAddScoped(static provider => new BlobStager(
                provider.GetRequiredService<IBlobKindRegistry>(),
                provider.GetRequiredService<IBlobStore>(),
                provider.GetService<IImageProcessor>(),
                provider.GetRequiredService<IGuardedBatchRunner>(),
                provider.GetRequiredService<IRequestContextAccessor>(),
                provider.GetRequiredService<IOptions<BlobOptions>>().Value,
                provider.GetRequiredService<ScratchDirectory>(),
                provider.GetRequiredService<BlobUploadMetrics>(),
                provider.GetRequiredService<ILogger<BlobStager>>()));
            services.TryAddScoped(static provider => new BlobResolver(
                provider.GetRequiredService<IBlobKindRegistry>(),
                provider.GetRequiredService<AccessEvaluator>(),
                provider.GetRequiredService<ISecurableRegistry>(),
                provider.GetRequiredService<IGuardedBatchRunner>(),
                provider.GetRequiredService<IRequestContextAccessor>(),
                provider.GetRequiredService<IQueryexSchemaProvider>(),
                provider.GetRequiredService<IEntityMetadataProvider>(),
                provider.GetRequiredService<StepUpBar>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<BlobResolver>>()));
            services.TryAddScoped<IBlobService>(static provider => new BlobService(
                provider.GetRequiredService<BlobStager>(), provider.GetRequiredService<BlobResolver>()));

            // --- The checks, after the built-in checks registered before them: the composition's, then the deployment's ---
            foreach (IStartupCheck check in new[] { new BlobsCheck(record, deployment: false), new BlobsCheck(record, deployment: true) })
            {
                record.BuiltInChecks.Add(check);
                services.AddSingleton(check);
            }
        }
    }
}
