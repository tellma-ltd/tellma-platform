// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Realtime;
using Tellma.Core.Composition;
using Tellma.Core.Notifications;

namespace Tellma.Core.AspNetCore
{
    /// <summary>Composes the realtime hub and selects its hosting mode.</summary>
    public static class TellmaRealtimeServiceCollectionExtensions
    {
        /// <summary>
        ///     Registers the realtime hub's services: SignalR with the hub's options and its one JSON
        ///     protocol, the user id provider, the connection tracker as a session and tenant-state
        ///     listener, the SignalR event publisher and the realtime status in place of the null ones,
        ///     the front door, the instruments, the hosting check and the <c>Tellma:Realtime</c>
        ///     options; <c>MapTellma</c> maps the hub. With <see cref="RealtimeOptions.Enabled" /> false
        ///     the publisher and the status resolve to the null ones and no hub is mapped.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The services are registered by the first call; every call applies its
        ///         <paramref name="configure" /> to the one SignalR builder. The web host calls it with
        ///         none after its composition, so a delegate a composer passed earlier is never
        ///         replaced.
        ///     </para>
        ///     <para>
        ///         A host selects its hosting mode by calling it on <c>TellmaBuilder.Services</c> from
        ///         its <c>composeWeb</c> delegate, with a delegate that adds a Redis backplane
        ///         (<c>AddStackExchangeRedis</c>) or Azure SignalR Service (<c>AddAzureSignalR</c>, as
        ///         <c>UseAzureWebDefaults()</c> does); without one the hub runs in-process. The host's
        ///         own package references decide the mode, so this package references neither.
        ///     </para>
        /// </remarks>
        /// <param name="services">The services, which <c>AddTellma</c> has composed or is composing.</param>
        /// <param name="configure">Configures the SignalR builder after the hub's options are set, such as to select a hosting mode.</param>
        /// <returns>The services, for chaining.</returns>
        /// <exception cref="InvalidOperationException">The services hold no Tellma composition.</exception>
        public static IServiceCollection AddTellmaRealtime(this IServiceCollection services, Action<ISignalRServerBuilder>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            // --- The first call registers everything; later calls only configure the builder ---
            RealtimeMarker? marker = services
                .Select(static descriptor => descriptor.ServiceType == typeof(RealtimeMarker) ? descriptor.ImplementationInstance : null)
                .OfType<RealtimeMarker>()
                .FirstOrDefault();
            if (marker is null)
            {
                marker = new RealtimeMarker(Register(services));
                _ = services.AddSingleton(marker);
            }

            configure?.Invoke(marker.Builder);

            // A delegate may add the framework's own JSON protocol again; the hub's stays the one named json.
            foreach (ServiceDescriptor plain in services.Where(static d => d.ServiceType == typeof(IHubProtocol) && d.ImplementationType == typeof(JsonHubProtocol)).ToList())
            {
                _ = services.Remove(plain);
            }

            return services;
        }

        /// <summary>Registers the hub's services, once.</summary>
        /// <param name="services">The services.</param>
        /// <returns>The SignalR builder.</returns>
        private static ISignalRServerBuilder Register(IServiceCollection services)
        {
            // --- The options, validated by the startup gate's options check ---
            _ = HostOptionsBinding.Bind<RealtimeOptions>(services, RealtimeOptions.SectionPath, TellmaWebApplicationBuilderExtensions.OptionsOwner);

            // --- SignalR, the hub's options and its one protocol: JSON under the platform's rules ---
            // The hub's own options, through the framework's setup: only options it marks as set
            // replace the global ones, filters and the receive limit included.
            ISignalRServerBuilder builder = services.AddSignalR()
                .AddJsonProtocol(static json => TellmaJson.Configure(json.PayloadSerializerOptions))
                .AddHubOptions<TellmaHub>(static hub =>
                {
                    // Clients send nothing but protocol frames, and no detail of a server failure reaches them.
                    hub.MaximumReceiveMessageSize = ClientEvent.MaxPayloadBytes;
                    hub.EnableDetailedErrors = false;
                    hub.SupportedProtocols = [TellmaJsonHubProtocol.ProtocolName];
                });

            // The intervals are read unvalidated: MapTellma builds the hub's handler, which reads
            // these options, before the startup gate's options check reports a bad value.
            _ = services.AddOptions<HubOptions<TellmaHub>>().Configure<IConfiguration>(static (hub, configuration) =>
            {
                RealtimeOptions realtime = HostSettings.Read<RealtimeOptions>(configuration, RealtimeOptions.SectionPath);
                hub.KeepAliveInterval = realtime.KeepAliveInterval;
                hub.ClientTimeoutInterval = realtime.ClientTimeoutInterval;
            });
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHubProtocol, TellmaJsonHubProtocol>());
            _ = services.Replace(ServiceDescriptor.Singleton<IUserIdProvider, TellmaUserIdProvider>());

            // --- The hosting mode, read from the composed services when first asked, and the instruments ---
            _ = services.AddSingleton(_ => RealtimeHosting.From(services));
            _ = services.AddSingleton<RealtimeMetrics>();
            _ = services.AddSingleton<HubFrontDoor>();

            // --- The tracker: this instance's connections, closed by the session and tenant-state listeners ---
            _ = services.AddSingleton<HubConnectionTracker>();
            _ = services.AddSingleton<ISessionTerminationListener>(static provider => provider.GetRequiredService<HubConnectionTracker>());
            _ = services.AddSingleton<ITenantStateListener>(static provider => provider.GetRequiredService<HubConnectionTracker>());

            // --- The publisher and the status, in place of Core's null ones, honouring Enabled ---
            _ = services.Replace(ServiceDescriptor.Scoped<IClientEventPublisher>(static provider =>
                provider.GetRequiredService<IOptions<RealtimeOptions>>().Value.Enabled
                    ? ActivatorUtilities.CreateInstance<SignalRClientEventPublisher>(provider)
                    : ActivatorUtilities.CreateInstance<NullClientEventPublisher>(provider)));
            _ = services.Replace(ServiceDescriptor.Singleton<IRealtimeStatus, RealtimeStatus>());

            // --- The hosting check ---
            _ = services.AddSingleton<IStartupCheck, RealtimeHostingCheck>();
            return builder;
        }

        /// <summary>Marks the services the realtime hub was composed into, and holds their SignalR builder.</summary>
        /// <param name="builder">The SignalR builder every call configures.</param>
        private sealed class RealtimeMarker(ISignalRServerBuilder builder)
        {
            /// <summary>The SignalR builder.</summary>
            public ISignalRServerBuilder Builder { get; } = builder;
        }
    }
}
