// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Identity;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Identity
{
    /// <summary>
    ///     The identity-server client as every composition registers it: one singleton, its options
    ///     bound from <c>Tellma:Identity</c> and settled by the platform (the service client id, and in
    ///     in-proc mode the authority), and judged by the startup gate's options check.
    /// </summary>
    public sealed class IdentityCompositionTests
    {
        /// <summary>The in-proc identity mode's configuration key.</summary>
        private const string ModeKey = "Tellma:Identity:Mode";

        /// <summary>The authority's configuration key.</summary>
        private const string AuthorityKey = "Tellma:Identity:Authority";

        /// <summary>The service client secret's configuration key.</summary>
        private const string SecretKey = "Tellma:Identity:ServiceClientSecret";

        [Fact]
        public async Task The_client_is_one_singleton_over_the_settled_options()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);

            IIdentityServerClient client = provider.GetRequiredService<IIdentityServerClient>();
            Assert.IsType<IdentityServerClient>(client);
            Assert.Same(client, provider.GetRequiredService<IIdentityServerClient>());

            IdentityServerClientOptions options = provider.GetRequiredService<IOptions<IdentityServerClientOptions>>().Value;
            Assert.Equal(
                ("acme-svc", "https://id.tellma.com/", "test-service-client-secret"),
                (options.ClientId, options.Authority?.AbsoluteUri, options.ServiceClientSecret));
        }

        [Fact]
        public async Task In_proc_mode_derives_the_authority_from_the_public_origin_whatever_the_section_says()
        {
            await using ServiceProvider provider = TestComposition.Compose(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound((ModeKey, "InProc"), (AuthorityKey, "https://elsewhere.tellma.com"))))
                .BuildServiceProvider(validateScopes: true);

            IdentityServerClientOptions options = provider.GetRequiredService<IOptions<IdentityServerClientOptions>>().Value;

            Assert.Equal("https://acme.app.tellma.com/id", options.Authority?.AbsoluteUri);
        }

        [Fact]
        public async Task The_sound_configuration_passes_the_options_check()
        {
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(CoreCheckNames.Options));
        }

        [Theory]
        [InlineData(AuthorityKey, null, "Authority is not set")]
        [InlineData(AuthorityKey, "http://id.tellma.com", "which is not https")]
        [InlineData(AuthorityKey, "/id", "not an absolute http or https URI")]
        [InlineData(AuthorityKey, "ftp://id.tellma.com", "not an absolute http or https URI")]
        [InlineData(AuthorityKey, "https://id.tellma.com/", "trailing slash")]
        [InlineData(AuthorityKey, "https://id.tellma.com?tenant=1", "a query")]
        [InlineData(AuthorityKey, "https://id.tellma.com#top", "a fragment")]
        [InlineData(SecretKey, null, "ServiceClientSecret is not set")]
        [InlineData(SecretKey, " ", "ServiceClientSecret is not set")]
        public async Task Unsound_options_fail_the_gate(string key, string? value, string expected)
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound((key, value))));

            Assert.Contains(problems.From(CoreCheckNames.Options), problem => problem.Problem.Contains(expected, StringComparison.Ordinal));
        }

        [Fact]
        public async Task An_authority_with_user_information_fails_the_gate_without_being_quoted()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound((AuthorityKey, "https://svc:hunter2@id.tellma.com"))));

            CompositionProblem problem = Assert.Single(problems.From(CoreCheckNames.Options));
            Assert.Contains("a URI with user information", problem.Problem, StringComparison.Ordinal);
            Assert.DoesNotContain("hunter2", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Development_may_reach_the_identity_server_over_http()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound((AuthorityKey, "http://localhost:5100"))),
                new TestEnvironment("Development"));

            Assert.Empty(problems.From(CoreCheckNames.Options));
        }

        [Theory]
        [InlineData("Development", false)]
        [InlineData("Production", true)]
        public async Task In_proc_mode_requires_the_service_secret_outside_development_only(string environment, bool required)
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound((ModeKey, "InProc"), (AuthorityKey, null), (SecretKey, null))),
                new TestEnvironment(environment));

            List<CompositionProblem> options = problems.From(CoreCheckNames.Options);
            Assert.Equal(required, options.Count == 1);
            Assert.All(options, static problem => Assert.Contains("ServiceClientSecret", problem.Problem, StringComparison.Ordinal));
        }

        [Fact]
        public async Task In_proc_mode_reports_an_authority_an_unsound_public_origin_leaves_underivable()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound((ModeKey, "InProc"), ("Tellma:PublicOrigin", "acme.app.tellma.com"))));

            Assert.Contains(problems.From(CoreCheckNames.Options), static problem => problem.Problem.Contains("cannot be derived", StringComparison.Ordinal));
        }

        [Fact]
        public async Task No_problem_quotes_the_service_secret()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound((AuthorityKey, "http://id.tellma.com"))));

            Assert.NotEmpty(problems.From(CoreCheckNames.Options));
            Assert.All(problems, static problem => Assert.DoesNotContain("test-service-client-secret", problem.Problem + problem.Fix, StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_call_without_settled_credentials_is_refused_before_anything_is_sent()
        {
            // The migrator in in-proc Development: the gate passes without a secret, and a call fails.
            using FakeIdentityServer server = new();
            IServiceCollection services = TestComposition.Compose(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound((ModeKey, "InProc"), (SecretKey, null))),
                new TestEnvironment("Development"));
            _ = services.AddHttpClient(IdentityServerClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => server);
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.GetRequiredService<IIdentityServerClient>().InviteAsync(IdentityClientHarness.Invitations(1), TestContext.Current.CancellationToken));

            Assert.Contains("ServiceClientSecret", exception.Message, StringComparison.Ordinal);
            Assert.Empty(server.Requests);
        }
    }
}
