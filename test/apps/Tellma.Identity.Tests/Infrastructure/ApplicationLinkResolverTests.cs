// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.Tests.Provisioning;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     Where the account pages' back link may point. The address a caller asks to return to
    ///     arrives on a URL anyone can write, and the link is the one place it is turned into
    ///     something a user clicks — so every case here is either an address on the client's own
    ///     registered origin, or one that must come out as that origin's root instead.
    /// </summary>
    public sealed class ApplicationLinkResolverTests : IAsyncDisposable
    {
        /// <summary>The origin the first-party client is registered at.</summary>
        private const string Origin = "https://acme.app.tellma.com";

        /// <summary>A first-party client with a registered origin.</summary>
        private const string FirstPartyClient = "acme-erp";

        /// <summary>A registered client that is not first-party.</summary>
        private const string ThirdPartyClient = "partner-reports";

        /// <summary>A first-party client that has no origin, such as a service.</summary>
        private const string OriginlessClient = "acme-jobs";

        /// <summary>The in-memory client registry.</summary>
        private readonly ServiceProvider _services = BuildServices();

        [Theory]
        [InlineData("https://acme.app.tellma.com/orders/42")]
        [InlineData("https://acme.app.tellma.com/orders?status=open&page=2#totals")]
        [InlineData("https://acme.app.tellma.com/")]
        public async Task A_return_address_on_the_clients_origin_becomes_the_link(string returnUrl)
        {
            ApplicationLink? link = await ResolveAsync(FirstPartyClient, returnUrl);

            Assert.NotNull(link);
            Assert.Equal(returnUrl, link.Url);
            Assert.Equal("Acme ERP", link.Name);
        }

        [Theory]

        // Another port is another origin, even on the same host.
        [InlineData("https://acme.app.tellma.com:8443/orders/42")]

        // Userinfo: the registered origin before an @, the real host after it — and the
        // harmless-looking form too, because nothing legitimate needs one.
        [InlineData("https://acme.app.tellma.com@evil.test/orders/42")]
        [InlineData("https://user:pass@acme.app.tellma.com/orders/42")]

        // Schemes a browser hands to something other than a page.
        [InlineData("javascript:alert(document.domain)")]
        [InlineData("data:text/html,<script>alert(1)</script>")]

        // Relative forms: the account pages are on the authority's origin, so a relative link
        // would resolve there, not at the client.
        [InlineData("/orders/42")]
        [InlineData("orders/42")]
        [InlineData("//evil.test/orders/42")]

        // Same host over the weaker transport, a neighbouring tenant, and a look-alike host.
        [InlineData("http://acme.app.tellma.com/orders/42")]
        [InlineData("https://other.app.tellma.com/orders/42")]
        [InlineData("https://acme.app.tellma.com.evil.test/orders/42")]
        public async Task A_return_address_anywhere_else_falls_back_to_the_origins_root(string returnUrl)
        {
            ApplicationLink? link = await ResolveAsync(FirstPartyClient, returnUrl);

            Assert.NotNull(link);
            Assert.Equal(Origin, link.Url);
            Assert.Equal("Acme ERP", link.Name);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Without_a_return_address_the_link_is_the_origins_root(string? returnUrl)
        {
            ApplicationLink? link = await ResolveAsync(FirstPartyClient, returnUrl);

            Assert.Equal(Origin, link?.Url);
        }

        [Fact]
        public async Task The_link_is_written_from_the_parsed_address()
        {
            // What was compared is what is emitted. A host in another case and a path with a raw
            // space come out in the one form every browser reads the same way.
            ApplicationLink? link = await ResolveAsync(FirstPartyClient, "HTTPS://ACME.App.Tellma.Com/orders/open items");

            Assert.Equal("https://acme.app.tellma.com/orders/open%20items", link?.Url);
        }

        [Fact]
        public async Task A_backslash_cannot_move_the_link_to_another_host()
        {
            // Browsers read a backslash in a web address as a slash; so does the parser. Whatever
            // the text suggested, the emitted address names the registered host and nothing else.
            ApplicationLink? link = await ResolveAsync(FirstPartyClient, @"https://acme.app.tellma.com\@evil.test/orders");

            Assert.NotNull(link);
            Uri emitted = new(link.Url);
            Assert.Equal("acme.app.tellma.com", emitted.Host);
            Assert.Empty(emitted.UserInfo);
            Assert.DoesNotContain('\\', link.Url);
        }

        [Theory]
        [InlineData("no-such-client")]
        [InlineData(ThirdPartyClient)]
        [InlineData(OriginlessClient)]
        [InlineData(null)]
        [InlineData("")]
        public async Task A_client_that_cannot_vouch_for_an_address_gets_the_configured_application(string? clientId)
        {
            // Unknown, third-party, or without an origin: there is no registration to hold the
            // return address against, so it is ignored however plausible it looks.
            ApplicationLink? link = await ResolveAsync(
                clientId, "https://acme.app.tellma.com/orders/42", applicationUrl: "https://erp.example.com/");

            Assert.NotNull(link);
            Assert.Equal("https://erp.example.com/", link.Url);
            Assert.Null(link.Name);
        }

        [Theory]
        [InlineData("no-such-client")]
        [InlineData(ThirdPartyClient)]
        [InlineData(OriginlessClient)]
        [InlineData(null)]
        public async Task With_no_client_to_vouch_and_nothing_configured_there_is_no_link(string? clientId)
        {
            Assert.Null(await ResolveAsync(clientId, "https://acme.app.tellma.com/orders/42"));
        }

        [Fact]
        public async Task A_third_partys_own_origin_is_never_offered()
        {
            // The third-party client is registered with an origin of its own, and a return address
            // on it. Neither is somewhere this UI sends a signed-in user.
            Assert.Null(await ResolveAsync(ThirdPartyClient, "https://reports.partner.test/dashboard"));
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
        }

        /// <summary>Resolves a back link against the registry and a configured fallback.</summary>
        /// <param name="clientId">The client the caller named.</param>
        /// <param name="returnUrl">The address the caller asked to return to.</param>
        /// <param name="applicationUrl">The deployment's configured application URL, when it has one.</param>
        /// <returns>The resolved link, or null.</returns>
        private async Task<ApplicationLink?> ResolveAsync(string? clientId, string? returnUrl, string? applicationUrl = null)
        {
            IOpenIddictApplicationManager manager = _services.GetRequiredService<IOpenIddictApplicationManager>();
            if (await manager.CountAsync(TestContext.Current.CancellationToken) == 0)
            {
                await RegisterAsync(manager, FirstPartyClient, "Acme ERP", firstParty: true, Origin);
                await RegisterAsync(manager, ThirdPartyClient, "Partner Reports", firstParty: false, "https://reports.partner.test");
                await RegisterAsync(manager, OriginlessClient, "Acme Jobs", firstParty: true, origin: null);
            }

            TellmaIdentityOptions options = new();
            options.Ui.ApplicationUrl = applicationUrl;

            ApplicationLinkResolver resolver = new(manager, Microsoft.Extensions.Options.Options.Create(options));
            return await resolver.ResolveAsync(clientId, returnUrl, TestContext.Current.CancellationToken);
        }

        /// <summary>Registers a client the way provisioning marks one.</summary>
        /// <param name="manager">The registry.</param>
        /// <param name="clientId">The client id.</param>
        /// <param name="displayName">The client's display name.</param>
        /// <param name="firstParty">Whether the client is marked first-party.</param>
        /// <param name="origin">The client's registered origin, when it has one.</param>
        private static async Task RegisterAsync(
            IOpenIddictApplicationManager manager, string clientId, string displayName, bool firstParty, string? origin)
        {
            OpenIddictApplicationDescriptor descriptor = new()
            {
                ClientId = clientId,
                ClientType = ClientTypes.Public,
                DisplayName = displayName,
            };

            if (origin is not null)
            {
                TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.Origin, origin);
            }

            if (firstParty)
            {
                TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.FirstParty, "true");
            }

            await manager.CreateAsync(descriptor, TestContext.Current.CancellationToken);
        }

        /// <summary>Builds the in-memory client registry the resolver reads.</summary>
        private static ServiceProvider BuildServices()
        {
            ServiceCollection services = new();
            services.AddInMemoryOpenIddict();
            return services.BuildServiceProvider();
        }
    }
}
