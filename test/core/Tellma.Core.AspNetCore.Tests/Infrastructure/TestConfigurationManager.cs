// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>
    ///     The identity server's metadata as a test decides it: what each read answers, by how many
    ///     refreshes were requested before it, or a failure when the decision is null. A read can also
    ///     stall, never answering and heeding no token, as the framework's manager does on its first read
    ///     of an identity server that does not answer.
    /// </summary>
    /// <param name="configuration">The metadata after a number of refreshes; null when it cannot be read.</param>
    internal sealed class TestConfigurationManager(Func<int, OpenIdConnectConfiguration?> configuration) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        /// <summary>How many refreshes were requested.</summary>
        private int _refreshes;

        /// <summary>How many reads were asked for.</summary>
        private int _reads;

        /// <summary>How many refreshes were requested.</summary>
        public int Refreshes => Volatile.Read(ref _refreshes);

        /// <summary>How many reads were asked for.</summary>
        public int Reads => Volatile.Read(ref _reads);

        /// <summary>Whether a read after a number of refreshes stalls; null when none does.</summary>
        public Func<int, bool>? Stalls { get; init; }

        /// <summary>Installs a manager in place of the static metadata the test host gives the sign-in scheme.</summary>
        /// <param name="services">The services.</param>
        /// <param name="manager">The manager.</param>
        public static void Install(IServiceCollection services, TestConfigurationManager manager)
        {
            _ = services.Configure<OpenIdConnectOptions>(TellmaAuthentication.OidcScheme, oidc =>
            {
                oidc.Configuration = null;
                oidc.ConfigurationManager = manager;
            });
        }

        /// <inheritdoc />
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            // A stalled read never completes; any other answers the test's decision, or fails without one.
            _ = Interlocked.Increment(ref _reads);
            return Stalls?.Invoke(Refreshes) == true
                ? new TaskCompletionSource<OpenIdConnectConfiguration>().Task
                : configuration(Refreshes) is { } current
                    ? Task.FromResult(current)
                    : throw new InvalidOperationException("The identity server's metadata cannot be read.");
        }

        /// <inheritdoc />
        public void RequestRefresh()
        {
            _ = Interlocked.Increment(ref _refreshes);
        }
    }
}
