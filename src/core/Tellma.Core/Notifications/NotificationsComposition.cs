// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Composition;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     Registers what notifications need beyond <see cref="CoreFeature" />'s contribution: the
    ///     options, the hub-less defaults of the client event publisher and the real-time status,
    ///     which a host that maps the hub replaces, and the notifier's process-wide machinery.
    /// </summary>
    internal static class NotificationsComposition
    {
        /// <summary>Registers the options, the hub-less defaults and the notifier's machinery.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, through the gate's validation ---
            options.Bind<NotificationsOptions>(NotificationsOptions.SectionPath, CoreFeature.FeatureName);

            // --- The hub-less defaults ---
            // TryAdd, so a host without the web package (the migrator, the offline suites) composes
            // and runs, and one that maps the hub keeps the publisher and status it registered.
            _ = services.AddLogging();
            services.TryAddScoped<IClientEventPublisher, NullClientEventPublisher>();
            services.TryAddSingleton<IRealtimeStatus, NullRealtimeStatus>();

            // --- The notifier's machinery: the channels that deliver and the instruments, one per process ---
            _ = services.AddSingleton<NotificationDeliveryChannels>();
            _ = services.AddSingleton<NotificationsMetrics>();
        }
    }
}
