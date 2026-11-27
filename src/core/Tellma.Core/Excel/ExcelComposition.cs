// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Blobs;
using Tellma.Core.Composition;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Registers the Excel codec's runtime: its options, bound from <c>Tellma:Excel</c> and
    ///     validated by the startup gate; the scratch directory, shared with the blobs; the exporter;
    ///     the scoped context builder and export runner the stacks' Excel operations use; the export
    ///     instruments; and the two Excel checks. The operations themselves are registered with the
    ///     stacks that serve them.
    /// </summary>
    internal static class ExcelComposition
    {
        /// <summary>Registers the options, the exporter, the export runner, the instruments and the checks.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="record">The realised composition: its configuration and its built-in checks.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, TellmaCompositionRecord record, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, through the gate's validation, and the scratch directory every workbook spools to ---
            options.Bind<ExcelOptions>(ExcelOptions.SectionPath, CoreFeature.FeatureName);
            services.TryAddSingleton(ScratchDirectory.From(record.Configuration));

            // --- The instruments, on the host's meter factory ---
            _ = services.AddMetrics();
            services.TryAddSingleton<ExcelMetrics>();

            // --- The exporter, one per process: it holds the limits and no request state ---
            // A factory, so the container's build-time validation leaves the options to the gate.
            services.TryAddSingleton<IExcelExporter>(static provider => new ExcelExporter(
                provider.GetRequiredService<IOptions<ExcelOptions>>().Value,
                provider.GetRequiredService<ScratchDirectory>().Path,
                provider.GetRequiredService<DeploymentVersions>().PlatformVersion,
                provider.GetRequiredService<IValueSetRegistry>()));

            // --- The importer, one per process, with the header indexes it caches for the life of the process ---
            services.TryAddSingleton<HeaderIndexCache>();
            services.TryAddSingleton<IExcelImporter>(static provider => new ExcelImporter(
                provider.GetRequiredService<IOptions<ExcelOptions>>().Value,
                provider.GetRequiredService<ScratchDirectory>().Path,
                provider.GetRequiredService<IBlobKindRegistry>(),
                provider.GetRequiredService<HeaderIndexCache>(),
                provider.GetRequiredService<ExcelMetrics>(),
                provider.GetRequiredService<ILoggerFactory>(),
                provider.GetRequiredService<IValueSetRegistry>(),
                provider.GetRequiredService<ICalendarRegistry>()));

            // --- Per scope: the request's context and the export and import runners over it ---
            services.TryAddScoped<ExcelContextFactory>();
            services.TryAddScoped<ExcelExports>();
            services.TryAddScoped<ExcelJobStarts>();

            // A factory, as the store is the host's to register: a composition without one still
            // builds and validates, and the operations resolve the runner only when an inspection
            // runs, so only an import fails to activate there.
            services.TryAddScoped(static provider => ActivatorUtilities.CreateInstance<ExcelImports>(provider));

            // --- The checks, after the built-in checks registered before them: the model's, then the deployment's ---
            foreach (IStartupCheck check in new[] { new ExcelCheck(deployment: false), new ExcelCheck(deployment: true) })
            {
                record.BuiltInChecks.Add(check);
                services.AddSingleton(check);
            }
        }
    }
}
