// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore;
using Tellma.Distro.Acme.TestSupport;
using Tellma.Identity.Data;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.IntegrationTests
{
    /// <summary>
    ///     The users' identity actions end to end against Acme's in-proc identity engine: an invite
    ///     answering each of the engine's outcomes and the delivery of the email it sent, a sandbox's
    ///     invite that only looks accounts up and welcomes the one it linked, and a service account whose issued credentials obtain
    ///     a token the tenant's API resolves to that account, until a rotation retires them.
    /// </summary>
    /// <param name="sql">The SQL Server the migrator and the host run against.</param>
    [Trait("Category", "Integration")]
    public sealed class UserIdentityTests(SqlServerFixture sql)
    {
        /// <summary>Acme's live seed tenant.</summary>
        private const int Live = 1;

        /// <summary>Acme's sandbox seed tenant, the live one's.</summary>
        private const int Sandbox = 2;

        /// <summary>The route of the test's bearer probe on the public API group.</summary>
        private const string WhoAmIRoute = "/whoami";

        /// <summary>The owner key the outbox correlates the emails it sends with.</summary>
        private const string OutboxOwnerKey = "outbox";

        /// <summary>How long the engine may take to send an invitation email it queued.</summary>
        private static readonly TimeSpan DeliveryBound = TimeSpan.FromSeconds(30);

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task An_invite_answers_the_engine_s_outcomes_and_the_status_reads_the_email_it_sent()
        {
            AcmeMigrator acme = new(sql, "acme_invite");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values);

            // --- An account the engine does not know, one active, one an operator disabled ---
            await CreateIdentityUserAsync(host, "active@example.com", UserLifecycleState.Active);
            await CreateIdentityUserAsync(host, "disabled@example.com", UserLifecycleState.Disabled);
            int fresh = await SaveIndividualAsync(host, Live, "Fresh", "fresh@example.com");
            int active = await SaveIndividualAsync(host, Live, "Active", "active@example.com");
            int disabled = await SaveIndividualAsync(host, Live, "Disabled", "disabled@example.com");

            IReadOnlyList<InviteResult> first = await InviteAsync(host, Live, fresh, active, disabled);

            Assert.Equal((fresh, InviteStatus.Invited, null), (first[0].Id, first[0].Status, first[0].Error));
            Assert.Equal((active, InviteStatus.Active, null), (first[1].Id, first[1].Status, first[1].Error));
            Assert.Equal(disabled, first[2].Id);
            Assert.Null(first[2].Status);
            Assert.False(string.IsNullOrWhiteSpace(first[2].Error), host.Warnings.ToString());

            // --- The fresh account holds no credential yet, so a second invite sends again ---
            IReadOnlyList<InviteResult> second = await InviteAsync(host, Live, fresh);
            Assert.Equal([new InviteResult(fresh, InviteStatus.Reinvited, null)], second);

            // --- The status: the email the engine sent, none expected for the active account, and the refusal ---
            IReadOnlyList<InvitationStatus> statuses = await AwaitSentAsync(host, fresh, active, disabled);
            Assert.Equal([fresh, active, disabled], statuses.Select(static status => status.Id));
            Assert.Equal((UserState.Invited, IdentityDeliveryState.Sent), (statuses[0].State, statuses[0].Delivery!.State));
            Assert.True(host.Emails.CountFor("fresh@example.com") >= 1);
            Assert.False(statuses[1].Delivery?.ExpectsDeliveryEvents ?? false);
            Assert.Equal((UserState.New, first[2].Error), (statuses[2].State, statuses[2].LastInviteError));
            Assert.Equal(0, host.Emails.CountFor("disabled@example.com"));
        }

        [Fact]
        public async Task A_sandbox_invite_links_an_existing_account_creates_nobody_and_welcomes_only_the_linked_user()
        {
            AcmeMigrator acme = new(sql, "acme_sandbox_invite");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values);
            await CreateIdentityUserAsync(host, "known@example.com", UserLifecycleState.Active);
            int known = await SaveIndividualAsync(host, Sandbox, "Known", "known@example.com");
            int stranger = await SaveIndividualAsync(host, Sandbox, "Stranger", "stranger@example.com");

            IReadOnlyList<InviteResult> results = await InviteAsync(host, Sandbox, known, stranger);

            Assert.Equal(
                [new InviteResult(known, InviteStatus.Active, null), new InviteResult(stranger, null, AccessCodes.SandboxRequiresExistingIdentity)],
                results);
            await using AsyncServiceScope identity = host.Services.CreateAsyncScope();
            Assert.Null(await identity.ServiceProvider.GetRequiredService<UserManager<TellmaIdentityUser>>().FindByEmailAsync("stranger@example.com"));

            // --- The identity engine emailed nobody: the only mail the host may send is the outbox's ---
            // The worker may already have sent the linked user's welcome, which the outbox correlates.
            Assert.DoesNotContain(host.Emails.Captured, static email => email.Message.Correlation?.OwnerKey != OutboxOwnerKey);

            // --- The linked user alone is welcomed, through the sandbox tenant's outbox ---
            Assert.Equal([known], await WelcomedAsync(acme, Sandbox));
        }

        [Fact]
        public async Task Issued_credentials_obtain_a_token_the_api_resolves_to_the_service_account_until_a_rotation_retires_them()
        {
            AcmeMigrator acme = new(sql, "acme_credentials");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values, new AcmeHostOptions { Map = MapWhoAmI });
            using HttpClient client = host.CreateBrowser(new CookieContainer());
            int robot = await SaveAsync(host, Live, new User { Kind = UserKind.Service, Name = "Robot" });

            CredentialsResult first = await IssueAsync(host, Live, robot);

            // --- The client's token reaches the tenant's API as the service account ---
            (string? token, string answer) = await TokenAsync(client, first);
            Assert.True(token is not null, answer);
            using (HttpRequestMessage request = new(HttpMethod.Get, new Uri($"/{Live}/api/v1{WhoAmIRoute}", UriKind.Relative)))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using HttpResponseMessage response = await client.SendAsync(request, Token);
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}\n{host.Warnings}");
                WhoAmI caller = (await response.Content.ReadFromJsonAsync<WhoAmI>(Token))!;
                Assert.Equal((nameof(PrincipalKind.ServiceAccount), robot), (caller.Kind, caller.UserId));
            }

            // --- A rotation retires the first client once it committed ---
            CredentialsResult second = await IssueAsync(host, Live, robot);
            Assert.NotEqual(first.ClientId, second.ClientId);
            Assert.Null((await TokenAsync(client, first)).Token);
            (string? rotated, string rotatedAnswer) = await TokenAsync(client, second);
            Assert.True(rotated is not null, rotatedAnswer);
        }

        // --- Helpers ---

        /// <summary>Maps the bearer probe, which answers who the request resolved to.</summary>
        /// <param name="endpoints">The host's groups.</param>
        private static void MapWhoAmI(TellmaEndpoints endpoints)
        {
            _ = endpoints.Api.MapGet(WhoAmIRoute, static (IRequestContextAccessor accessor) => Results.Json(new WhoAmI(accessor.Current.Kind.ToString(), accessor.Current.UserId)))
                .WithMutation(false)
                .AllowMember();
        }

        /// <summary>Asks the engine for a client-credentials token for the tenant API at the public origin.</summary>
        /// <param name="client">A client at the public origin.</param>
        /// <param name="credentials">The credentials.</param>
        /// <returns>The access token, null when the engine refused the credentials, and the engine's answer.</returns>
        private static async Task<(string? Token, string Answer)> TokenAsync(HttpClient client, CredentialsResult credentials)
        {
            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret,
                ["scope"] = "tellma_api",
                ["resource"] = AcmeMigrator.PublicOrigin,
            });
            using HttpResponseMessage response = await client.PostAsync(new Uri("/id/connect/token", UriKind.Relative), form, Token);
            string answer = await response.Content.ReadAsStringAsync(Token);
            if (!response.IsSuccessStatusCode)
            {
                return (null, answer);
            }

            // The answer carries the token itself, so a success reports only its status.
            using var document = JsonDocument.Parse(answer);
            return (document.RootElement.GetProperty("access_token").GetString(), ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        ///     Creates an account at the in-proc engine in a lifecycle state, holding a password, so the
        ///     engine answers an invitation to it as a known account rather than an unfinished one.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="email">The account's email.</param>
        /// <param name="state">The account's state.</param>
        /// <returns>A task that completes when the account exists.</returns>
        private static async Task CreateIdentityUserAsync(AcmeHost host, string email, UserLifecycleState state)
        {
            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            UserManager<TellmaIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<TellmaIdentityUser>>();
            TellmaIdentityUser account = new()
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = email.Split('@')[0],
                Locale = "en",
                LifecycleState = state,
                CreatedUtc = DateTimeOffset.UtcNow,
            };

            // A throwaway password for this test's account alone: the engine counts it as a credential.
            IdentityResult created = await users.CreateAsync(account, "Aa1!" + Guid.NewGuid().ToString("N"));
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(static error => error.Description)));
        }

        /// <summary>The users a tenant's outbox holds a welcome email for, one entry per email, by id.</summary>
        /// <param name="acme">The deployment.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The recipients' ids.</returns>
        private static async Task<IReadOnlyList<int>> WelcomedAsync(AcmeMigrator acme, int tenantId)
        {
            await using SqlConnection connection = new(acme.TenantConnectionString(tenantId));
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT [RecipientUserId] FROM [core].[OutgoingEmails] WHERE [RegardingEntity] = N'{CoreResources.User}' ORDER BY [Id];";
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            List<int> recipients = [];
            while (await reader.ReadAsync(Token))
            {
                recipients.Add(reader.GetInt32(0));
            }

            return recipients;
        }

        /// <summary>Saves an active individual in a tenant as the system.</summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="name">The name.</param>
        /// <param name="email">The email.</param>
        /// <returns>The user's id.</returns>
        private static Task<int> SaveIndividualAsync(AcmeHost host, int tenantId, string name, string email)
        {
            return SaveAsync(host, tenantId, new User { Kind = UserKind.Individual, Name = name, Email = email });
        }

        /// <summary>Saves a new user in a tenant as the system.</summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="user">The user.</param>
        /// <returns>The user's id.</returns>
        private static async Task<int> SaveAsync(AcmeHost host, int tenantId, User user)
        {
            await using TenantScope scope = await SystemScopeAsync(host, tenantId);
            EntitiesResult<User> saved = await Users(scope).SaveAsync([user], new SaveOptions { ReturnEntities = false }, Token);
            return checked((int)Assert.Single(saved.Ids));
        }

        /// <summary>Invites users of a tenant as the system.</summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="ids">The users.</param>
        /// <returns>The results.</returns>
        private static async Task<IReadOnlyList<InviteResult>> InviteAsync(AcmeHost host, int tenantId, params int[] ids)
        {
            await using TenantScope scope = await SystemScopeAsync(host, tenantId);
            return await Users(scope).ExecuteActionAsync<IReadOnlyList<InviteResult>>("invite", ids, null, new ActionOptions(), Token);
        }

        /// <summary>Issues a service account's credentials as the system.</summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="id">The account.</param>
        /// <returns>The credentials.</returns>
        private static async Task<CredentialsResult> IssueAsync(AcmeHost host, int tenantId, int id)
        {
            await using TenantScope scope = await SystemScopeAsync(host, tenantId);
            return await Users(scope).ExecuteActionAsync<CredentialsResult>("issue-credentials", [id], null, new ActionOptions(), Token);
        }

        /// <summary>Reads the live tenant's invitation status until the first user's email is reported sent.</summary>
        /// <param name="host">The host.</param>
        /// <param name="ids">The users; the first is the one whose email is awaited.</param>
        /// <returns>The last status read.</returns>
        private static async Task<IReadOnlyList<InvitationStatus>> AwaitSentAsync(AcmeHost host, params int[] ids)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + DeliveryBound;
            while (true)
            {
                await using TenantScope scope = await SystemScopeAsync(host, Live);
                StackDescriptor users = scope.Services.GetRequiredService<IStackRegistry>().Find(CoreResources.User)!;
                ApiActionDescriptor status = users.Actions.OfType<ApiActionDescriptor>().Single(static action => action.Name == "invitation-status");
                IdsRequest request = new([.. ids.Select(static id => (long)id)]);
                var answered = (IReadOnlyList<InvitationStatus>)(await scope.Services.GetRequiredService<IApiActionInvoker>().InvokeAsync(status, request, Token))!;
                if (answered[0].Delivery?.State == IdentityDeliveryState.Sent || DateTimeOffset.UtcNow > deadline)
                {
                    return answered;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), Token);
            }
        }

        /// <summary>A scope of a tenant as the system principal, as the migrator's tenant work runs.</summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The scope.</returns>
        private static Task<TenantScope> SystemScopeAsync(AcmeHost host, int tenantId)
        {
            RequestContextSnapshot system = new(tenantId, PrincipalKind.System, "system", WellKnownIds.SystemUserId, "worker", TraceParent: null);
            return host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(system, allowNonActive: false, Token);
        }

        /// <summary>The users' service of a scope.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<User, int> Users(TenantScope scope)
        {
            return scope.Services.GetRequiredService<EntityService<User, int>>();
        }

        /// <summary>Who a request resolved to.</summary>
        /// <param name="Kind">The principal's kind.</param>
        /// <param name="UserId">The tenant user.</param>
        private sealed record WhoAmI(string Kind, int? UserId);
    }
}
