// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     Registers the process's string localization: the framework's localizers, rendered through
    ///     ICU, and the keys the neutral shared resources declare.
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

            // --- The keys the neutral shared resources declare, which the client metadata's label keys follow ---
            // TryAdd: the string pack's composition, which merges the same resources, may answer instead.
            services.TryAddSingleton<INeutralStringKeys, NeutralStringKeys>();
        }
    }
}
