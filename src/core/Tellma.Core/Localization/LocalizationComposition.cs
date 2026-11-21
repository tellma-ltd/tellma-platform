// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     Registers the process's string localization: the framework's localizers, rendered through
    ///     ICU, the string packs merged from every composed feature's shared resources, and the
    ///     labels resolved against them.
    /// </summary>
    internal static class LocalizationComposition
    {
        /// <summary>
        ///     Registers the framework's localization services and makes
        ///     <see cref="IcuStringLocalizerFactory" /> the one <see cref="IStringLocalizerFactory" />,
        ///     whatever factory the host registered before.
        /// </summary>
        /// <param name="services">The host's services.</param>
        public static void Register(IServiceCollection services)
        {
            // --- The framework's services: IStringLocalizer<T> over whichever factory is registered ---
            _ = services.AddLogging();
            _ = services.AddMetrics();
            _ = services.AddLocalization();

            // --- The factory itself, replacing the framework's and any other ---
            // Every localizer in the process, typed or created by base name, must render ICU, so no
            // second factory may survive: a host that registers one later through TryAdd (as
            // AddLocalization does) keeps this one.
            _ = services.RemoveAll<IStringLocalizerFactory>();
            services.AddSingleton<IStringLocalizerFactory, IcuStringLocalizerFactory>();

            // --- The string packs: every composed feature's shared resources, merged per language (spec 0013 §10.4) ---
            // One composer answers for the packs and for the keys the neutral resources declare,
            // which the client metadata's label keys follow.
            services.TryAddSingleton<StringPackComposer>();
            services.TryAddSingleton<IStringPackProvider>(static provider => provider.GetRequiredService<StringPackComposer>());
            services.TryAddSingleton<INeutralStringKeys>(static provider => provider.GetRequiredService<StringPackComposer>());

            // --- The composition's C# enums, each named once (spec 0029 §2.2) ---
            // The labels and the client metadata name and label an enum alike. The setting keys and
            // the stacks are optional, so a host that composes neither still labels its entities' enums.
            services.TryAddSingleton(static provider => new NamedEnums(
                provider.GetRequiredService<IEntityMetadataProvider>(),
                provider.GetService<ISettingKeyRegistry>(),
                provider.GetService<IStackRegistry>()));

            // --- Labels by convention, resolved against the merged resources (spec 0013 §7.6) ---
            services.TryAddSingleton<ILabelProvider, LabelProvider>();

            // --- Any shared resource by its key, through its owner's localizer: what a response renders for readers without a pack ---
            services.TryAddSingleton<ResourceTexts>();
        }
    }
}
