// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Reports;
using Tellma.Core.Composition;
using Tellma.Core.Localization;

namespace Tellma.Core.Reports
{
    /// <summary>
    ///     Registers what the stored reports' stack needs beyond <see cref="CoreFeature" />'s
    ///     contribution: its options, bound from <c>Tellma:Reports</c> and validated by the startup
    ///     gate; the read-through into dashboards, empty unless the dashboards' feature registers its
    ///     own; and the service's collaborators.
    /// </summary>
    internal static class ReportsComposition
    {
        /// <summary>Registers the options, the read-through and the service's collaborators.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="record">The realised composition, whose features' names reserve the seeds' codes.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, TellmaCompositionRecord record, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, through the gate's validation: a value out of range fails startup ---
            options.Bind<ReportsOptions>(ReportsOptions.SectionPath, CoreFeature.FeatureName);

            // --- No dashboard shows a report until the dashboards' feature says otherwise ---
            services.TryAddScoped<IDashboardReadThrough, NullDashboardReadThrough>();

            // --- The service's bundle, one per process: its collaborators are all singletons ---
            // spec 0033 §13: a seed's code starts with its feature's name and a dot.
            string[] prefixes = [.. record.Features.Select(static feature => feature.Name + ".").Distinct(StringComparer.OrdinalIgnoreCase)];
            services.TryAddSingleton(provider => new ReportServiceDependencies(
                new ReportTitles(
                    provider.GetRequiredService<ResourceTexts>(),
                    provider.GetRequiredService<ILabelProvider>(),
                    provider.GetRequiredService<ILocalizationNegotiator>(),
                    provider.GetRequiredService<IEntityMetadataProvider>()),
                prefixes));
        }
    }
}
