// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Composition;

namespace Tellma.Core.Identity
{
    /// <summary>
    ///     Registers what the identity-server client needs beyond <see cref="CoreFeature" />'s
    ///     registration of the client itself: its options, settled and validated, and the named HTTP
    ///     client it sends through. Every host that composes Tellma gets them, the web host and the
    ///     migrator alike.
    /// </summary>
    internal static class IdentityComposition
    {
        /// <summary>The configuration key of the identity mode, which the web host binds as an enum.</summary>
        internal const string ModeKey = IdentityServerClientOptions.SectionPath + ":Mode";

        /// <summary>The mode that hosts the identity engine inside the distribution's web host.</summary>
        internal const string InProcMode = "InProc";

        /// <summary>The configuration key of the distribution's public origin.</summary>
        private const string PublicOriginKey = "Tellma:PublicOrigin";

        /// <summary>Registers the options and the HTTP client.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        /// <param name="slug">The distribution's slug, which names its service client.</param>
        /// <param name="configuration">The host's configuration.</param>
        /// <param name="environment">The host's environment.</param>
        public static void Register(
            IServiceCollection services, OptionsRegistrar options, string slug, IConfiguration configuration, IHostEnvironment environment)
        {
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, from the section the web host's sign-in options bind from too ---
            options.Bind<IdentityServerClientOptions>(IdentityServerClientOptions.SectionPath, CoreFeature.FeatureName);
            AddServices(services, slug, configuration, environment);
        }

        /// <summary>Registers everything but the binding: the settling, the validator and the HTTP client.</summary>
        /// <param name="services">The services.</param>
        /// <param name="slug">The distribution's slug.</param>
        /// <param name="configuration">The host's configuration.</param>
        /// <param name="environment">The host's environment.</param>
        internal static void AddServices(IServiceCollection services, string slug, IConfiguration configuration, IHostEnvironment environment)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(slug);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(environment);

            // --- What the platform sets after binding: the service client id, and in-proc the authority ---
            _ = services.AddOptions<IdentityServerClientOptions>().PostConfigure(settled => Settle(settled, slug, configuration));

            // --- Validated by the startup gate's options check, with the settled values ---
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IdentityServerClientOptions>>(
                new IdentityServerClientOptionsValidator(environment, configuration)));

            // --- The named HTTP client: the client times each exchange itself on the injected clock ---
            // The factory's own request logging is removed: at Trace it writes the request headers,
            // and while their formatted text honours header redaction, their structured values carry
            // the Authorization header verbatim, the bearer token with it. The client logs each call
            // itself, without a credential.
            _ = services
                .AddHttpClient(IdentityServerClient.HttpClientName, static client => client.Timeout = Timeout.InfiniteTimeSpan)
                .RemoveAllLoggers();

            // --- The clock, the instruments and the logger; each is idempotent ---
            services.TryAddSingleton(TimeProvider.System);
            _ = services.AddMetrics();
            _ = services.AddLogging();
        }

        /// <summary>Whether the configuration runs the identity engine in-proc.</summary>
        /// <param name="configuration">The host's configuration.</param>
        /// <returns>True in in-proc identity mode.</returns>
        internal static bool IsInProc(IConfiguration configuration)
        {
            return string.Equals(configuration[ModeKey], InProcMode, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Sets what is not configuration: the service client id, <c>&lt;slug&gt;-svc</c>, the audience of
        ///     the service accounts it creates, the public origin, and in in-proc mode the authority, the
        ///     in-proc engine's issuer under the public origin.
        /// </summary>
        /// <param name="options">The bound options.</param>
        /// <param name="slug">The distribution's slug.</param>
        /// <param name="configuration">The host's configuration.</param>
        internal static void Settle(IdentityServerClientOptions options, string slug, IConfiguration configuration)
        {
            options.ClientId = slug + IdentityServerClientOptions.ServiceClientSuffix;

            // The public origin's own startup check reports a bad origin.
            string? origin = configuration[PublicOriginKey];
            options.ServiceAccountResource = string.IsNullOrWhiteSpace(origin) ? null : origin;
            if (IsInProc(configuration))
            {
                // The validator reports the authority a bad origin leaves underivable.
                options.Authority = origin is { Length: > 0 } && !origin.EndsWith('/')
                    && origin.Contains("://", StringComparison.Ordinal)
                    && Uri.TryCreate(origin + IdentityServerClientOptions.InProcPath, UriKind.Absolute, out Uri? issuer)
                    ? issuer
                    : null;
            }
        }
    }
}
