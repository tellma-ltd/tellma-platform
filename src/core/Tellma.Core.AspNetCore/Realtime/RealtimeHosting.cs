// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>How the hub reaches its connections.</summary>
    internal enum RealtimeMode
    {
        /// <summary>The framework's own lifetime manager: this instance's connections alone.</summary>
        InProcess,

        /// <summary>A Redis backplane, which carries every send to every instance.</summary>
        Redis,

        /// <summary>Azure SignalR Service, which holds every client connection itself.</summary>
        Azure,

        /// <summary>A lifetime manager the platform does not know, which the hosting check refuses.</summary>
        Unknown,
    }

    /// <summary>
    ///     The hosting mode the composed services select, read from the hub's lifetime manager
    ///     registration, and the Azure SignalR Service options type when its SDK is composed. Both are
    ///     located by type name, so this assembly references neither the Redis backplane nor the SDK.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <param name="lifetimeManager">The registered lifetime manager's type; null when none is registered.</param>
    /// <param name="azureOptionsType">The SDK's options type, when the SDK is composed.</param>
    internal sealed class RealtimeHosting(RealtimeMode mode, Type? lifetimeManager, Type? azureOptionsType)
    {
        /// <summary>The full name of Azure SignalR Service's lifetime manager.</summary>
        public const string AzureLifetimeManagerName = "Microsoft.Azure.SignalR.ServiceLifetimeManager`1";

        /// <summary>The full name of the Redis backplane's lifetime manager.</summary>
        public const string RedisLifetimeManagerName = "Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisHubLifetimeManager`1";

        /// <summary>The full name of Azure SignalR Service's options.</summary>
        public const string AzureOptionsName = "Microsoft.Azure.SignalR.ServiceOptions";

        /// <summary>The name of the options' application name property.</summary>
        public const string ApplicationNameProperty = "ApplicationName";

        /// <summary>The mode.</summary>
        public RealtimeMode Mode { get; } = mode;

        /// <summary>The registered lifetime manager's type; null when none is registered.</summary>
        public Type? LifetimeManager { get; } = lifetimeManager;

        /// <summary>The mode as the connections instrument tags it.</summary>
        public string ModeTag => Mode switch
        {
            RealtimeMode.InProcess => "in-process",
            RealtimeMode.Redis => "redis",
            RealtimeMode.Azure => "azure",
            RealtimeMode.Unknown => "other",
            _ => "other",
        };

        /// <summary>Whether every instance's connections receive a send made on any instance.</summary>
        public bool ReachesEveryInstance => Mode is RealtimeMode.Redis or RealtimeMode.Azure;

        /// <summary>Reads the mode from the composed services.</summary>
        /// <param name="services">The services, read when the host first asks; by then every registration is made.</param>
        /// <returns>The hosting.</returns>
        public static RealtimeHosting From(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // --- The lifetime manager the hub resolves: the last closed registration, else the last open one ---
            ServiceDescriptor? descriptor = services.LastOrDefault(static d => d.ServiceType == typeof(HubLifetimeManager<TellmaHub>))
                ?? services.LastOrDefault(static d => d.ServiceType == typeof(HubLifetimeManager<>));
            Type? manager = descriptor?.ImplementationType;
            Type? definition = manager is { IsGenericType: true } ? manager.GetGenericTypeDefinition() : manager;
            RealtimeMode mode = definition switch
            {
                null => RealtimeMode.Unknown,
                _ when definition == typeof(DefaultHubLifetimeManager<>) => RealtimeMode.InProcess,
                _ when definition.FullName == AzureLifetimeManagerName => RealtimeMode.Azure,
                _ when definition.FullName == RedisLifetimeManagerName => RealtimeMode.Redis,
                _ => RealtimeMode.Unknown,
            };

            // --- The SDK's options type, from any options registration of it ---
            Type? azureOptions = services
                .Select(static d => d.ServiceType)
                .Where(static type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IConfigureOptions<>))
                .Select(static type => type.GetGenericArguments()[0])
                .FirstOrDefault(static type => type.FullName == AzureOptionsName);

            return new RealtimeHosting(mode, manager, azureOptions);
        }

        /// <summary>The application name Azure SignalR Service's options hold.</summary>
        /// <param name="provider">The host's services.</param>
        /// <returns>The name; null when the SDK is not composed or names no application.</returns>
        public string? AzureApplicationName(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (azureOptionsType is null)
            {
                return null;
            }

            // The options are read through the SDK's own setup, so a name configured any way counts.
            object? options = provider.GetService(typeof(IOptions<>).MakeGenericType(azureOptionsType));
            object? value = options?.GetType().GetProperty(nameof(IOptions<>.Value))?.GetValue(options);
            return value?.GetType().GetProperty(ApplicationNameProperty)?.GetValue(value) as string;
        }
    }
}
