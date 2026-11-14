// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Hosting
{
    /// <summary>
    ///     The web host's identity-server client: in in-proc Development without a configured service
    ///     secret it authenticates with the one the host generates for its boot, the one the in-proc
    ///     engine seeds its service client with; a configured secret is used as it is.
    /// </summary>
    public sealed class IdentityClientHostTests
    {
        [Fact]
        public async Task In_proc_development_without_a_service_secret_uses_the_boot_s_generated_one()
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(static options =>
            {
                options.Environment = "Development";
                _ = options.Settings.Remove("Tellma:Identity:Authority");
                options.Settings["Tellma:Identity:Mode"] = "InProc";
                options.Settings["TellmaIdentity:Keys:Signing:Source"] = "DevelopmentSelfSigned";
                options.Settings["TellmaIdentity:Keys:Encryption:Source"] = "DevelopmentSelfSigned";
                options.Settings["TellmaIdentity:Development:AllowDevelopmentCertificates"] = "true";
            });
            await using WebApplication host = app;

            IdentityServerClientOptions client = host.Services.GetRequiredService<IOptions<IdentityServerClientOptions>>().Value;
            string? generated = host.Services.GetRequiredService<IdentityClientSecrets>().ServiceClientSecret;

            Assert.False(string.IsNullOrWhiteSpace(generated));
            Assert.Equal(
                (TellmaTestHost.PublicOrigin + "/id", TellmaTestHost.Slug + "-svc", generated),
                (client.Authority?.AbsoluteUri, client.ClientId, client.ServiceClientSecret));
        }

        [Fact]
        public async Task A_configured_service_secret_is_used_as_it_is()
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(static options => options.Settings["Tellma:Identity:ServiceClientSecret"] = "configured-secret");
            await using WebApplication host = app;

            IdentityServerClientOptions client = host.Services.GetRequiredService<IOptions<IdentityServerClientOptions>>().Value;

            Assert.Equal(
                (TellmaTestHost.Issuer, "configured-secret"),
                (client.Authority?.OriginalString, client.ServiceClientSecret));
        }
    }
}
