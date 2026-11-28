// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     The startup check of the distribution's sign-in client at its identity server. The BFF client
    ///     always needs a secret, because it signs users in as a confidential client; a standalone server's
    ///     is the one onboarding issued. In-proc, it must be stable outside Development, because several
    ///     instances seed and use it and a per-boot secret would break the others; and the engine sends
    ///     its mail through the host's email pipeline, which must be composed. The service client's
    ///     secret is validated with the identity-server client's options, in every host.
    /// </summary>
    /// <param name="secrets">The secrets as the host resolved them.</param>
    /// <param name="configuration">The application's configuration.</param>
    internal sealed class IdentityCheck(IdentityClientSecrets secrets, IConfiguration configuration) : IStartupCheck
    {
        /// <summary>The check's name.</summary>
        public const string CheckName = "host.identity";

        /// <inheritdoc />
        public string Name => CheckName;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            List<CompositionProblem> problems = [];
            IdentityMode mode = HostSettings.Read<IdentityHostOptions>(configuration, IdentityHostOptions.SectionPath).Mode;
            const string Prefix = IdentityHostOptions.SectionPath + ":";

            // --- The BFF client's secret ---
            if (secrets.ClientSecret is null)
            {
                problems.Add(mode == IdentityMode.InProc
                    ? new CompositionProblem(
                        CheckName,
                        $"{Prefix}{nameof(IdentityHostOptions.ClientSecret)} is not set; outside Development the in-proc identity engine needs a stable secret for the distribution's sign-in client, shared by every instance.",
                        $"Set {Prefix}{nameof(IdentityHostOptions.ClientSecret)} from the deployment's secret store.")
                    : new CompositionProblem(
                        CheckName,
                        $"{Prefix}{nameof(IdentityHostOptions.ClientSecret)} is not set; the distribution signs users in as a confidential client of its identity server.",
                        $"Set {Prefix}{nameof(IdentityHostOptions.ClientSecret)} to the secret the identity server issued for the distribution's client."));
            }

            // The service client's secret is the identity-server client's option, which the options
            // check validates in the web host and the migrator alike, in either identity mode.

            // --- In-proc, the engine's mail goes through the host's email pipeline ---
            if (mode == IdentityMode.InProc && services.GetService<IServiceProviderIsService>()?.IsService(typeof(IEmailSender)) != true)
            {
                problems.Add(new CompositionProblem(
                    CheckName,
                    $"{Prefix}{nameof(IdentityHostOptions.Mode)} is InProc, but no {nameof(IEmailSender)} is registered; the in-proc identity engine sends its invitations and sign-in codes through the host's email pipeline.",
                    "Compose the email feature (AddTellmaEmail) in the distribution's web host."));
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
