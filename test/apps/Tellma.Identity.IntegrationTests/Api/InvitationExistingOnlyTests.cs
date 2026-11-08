// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Core.Abstractions.Email;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Email;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.IntegrationTests.Api
{
    /// <summary>
    ///     The bulk invite's <c>existingOnly</c> items: a lookup by email that answers with the
    ///     user's subject or a stable error code, and never creates a user, issues a link or queues
    ///     an email — whatever the rest of the batch does.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class InvitationExistingOnlyTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task A_mixed_batch_answers_each_lookup_and_queues_mail_for_the_ordinary_invite_alone()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idexonly");
            var dispatcher = RecordingEmailDispatcher.Install(factory);

            // One user for each row of the outcome table, plus an orphaned user with a credential.
            TellmaIdentityUser withCredential = await TestData.CreateActiveUserAsync(factory, "member@example.com");
            await TestData.AddPasswordAsync(factory, withCredential);
            TellmaIdentityUser withoutCredential = await TestData.CreateActiveUserAsync(factory, "pending@example.com");
            TellmaIdentityUser disabled = await TestData.CreateActiveUserAsync(factory, "disabled@example.com");
            await TestData.AddPasswordAsync(factory, disabled);
            await TestData.SetLifecycleStateAsync(factory, disabled, UserLifecycleState.Disabled);
            TellmaIdentityUser orphaned = await TestData.CreateActiveUserAsync(factory, "orphan@example.com");
            await TestData.AddPasswordAsync(factory, orphaned);
            await TestData.SetLifecycleStateAsync(factory, orphaned, UserLifecycleState.Orphaned);
            TellmaIdentityUser lapsed = await TestData.CreateActiveUserAsync(factory, "lapsed@example.com");
            await TestData.SetLifecycleStateAsync(factory, lapsed, UserLifecycleState.Orphaned);

            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await TestData.GetIdentityScopeTokenAsync(factory));

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/v1/invitations", UriKind.Relative),
                new
                {
                    users = new object[]
                    {
                        // An off-origin return URL would refuse an ordinary invite; a lookup issues
                        // no link, so the URL is never read and the answer is still Active.
                        new { email = "member@example.com", existingOnly = true, returnUrl = "https://evil.example/" },
                        new { email = "pending@example.com", existingOnly = true },
                        new { email = "nobody@example.com", existingOnly = true },
                        new { email = "disabled@example.com", existingOnly = true },
                        new { email = "orphan@example.com", existingOnly = true },
                        new { email = "lapsed@example.com", existingOnly = true },
                        new { email = "fresh@example.com", existingOnly = false },
                    },
                },
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            using var document = JsonDocument.Parse(body);
            JsonElement[] results = [.. document.RootElement.GetProperty("results").EnumerateArray()];
            Assert.Equal(
                ["member@example.com", "pending@example.com", "nobody@example.com", "disabled@example.com", "orphan@example.com", "lapsed@example.com", "fresh@example.com"],
                results.Select(static r => r.GetProperty("email").GetString()));

            // A user who holds a credential: Active, with their subject.
            AssertActive(results[0], withCredential.Id);

            // A user without a credential, and an address nobody holds: the stable codes, with no
            // status and no subject.
            AssertError(results[1], "existing_user_has_no_credential");
            AssertError(results[2], "unknown_email");

            // A disabled user: the refusal an ordinary invite gives, without naming the state.
            string refusal = AssertError(results[3], expected: null);
            Assert.Contains("operator", refusal, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Disabled", refusal, StringComparison.Ordinal);

            // An orphaned user with a credential: Active, restored as an ordinary invite restores.
            AssertActive(results[4], orphaned.Id);

            // An orphaned user without a credential: the credential error, and no restore.
            AssertError(results[5], "existing_user_has_no_credential");

            // The ordinary invite in the same batch proceeds as always.
            Assert.Equal("Invited", results[6].GetProperty("status").GetString());

            // The ordinary invite's mail arrives; the whole batch was one hand-off, so once it has
            // been queued nothing else of this batch can still be on its way.
            await factory.Emails.WaitForLinkAsync("fresh@example.com");
            Assert.Equal(["fresh@example.com"], dispatcher.Recipients());

            // No lookup created a user, issued a single-use code, or changed a lifecycle state it
            // was not meant to change.
            using IServiceScope scope = factory.Services.CreateScope();
            UserManager<TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<TellmaIdentityUser>>();
            Assert.Null(await userManager.FindByEmailAsync("nobody@example.com"));
            Assert.Equal(UserLifecycleState.Active, (await userManager.FindByIdAsync(withoutCredential.Id))!.LifecycleState);
            Assert.Equal(UserLifecycleState.Disabled, (await userManager.FindByIdAsync(disabled.Id))!.LifecycleState);
            Assert.Equal(UserLifecycleState.Active, (await userManager.FindByIdAsync(orphaned.Id))!.LifecycleState);
            Assert.Equal(UserLifecycleState.Orphaned, (await userManager.FindByIdAsync(lapsed.Id))!.LifecycleState);

            TellmaIdentityDbContext store = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            string[] lookedUp = [withCredential.Id, withoutCredential.Id, disabled.Id, orphaned.Id, lapsed.Id];
            Assert.False(await store.Set<SingleUseCode>()
                .AnyAsync(code => lookedUp.Contains(code.UserId), TestContext.Current.CancellationToken));

            // The one restore is audited with the distribution whose request caused it, and the
            // credential-less orphan has no lifecycle row at all.
            AuditEvent restore = Assert.Single(await store.Set<AuditEvent>()
                .Where(static e => e.Action == "UserLifecycleChanged")
                .ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal(orphaned.Id, restore.Subject);
            Assert.Equal("acme-svc", restore.ClientId);
        }

        [Fact]
        public async Task A_sandbox_style_invite_creates_no_user_and_queues_no_mail()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idexsandbox");
            var dispatcher = RecordingEmailDispatcher.Install(factory);

            TellmaIdentityUser member = await TestData.CreateActiveUserAsync(factory, "member@example.com");
            await TestData.AddPasswordAsync(factory, member);
            await TestData.CreateActiveUserAsync(factory, "pending@example.com");

            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await TestData.GetIdentityScopeTokenAsync(factory));

            int usersBefore = await CountUsersAsync(factory);

            // Every item of the batch is a lookup, as a sandbox tenant sends it.
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/v1/invitations", UriKind.Relative),
                new
                {
                    users = new[]
                    {
                        new { email = "member@example.com", existingOnly = true },
                        new { email = "pending@example.com", existingOnly = true },
                        new { email = "stranger@example.com", existingOnly = true },
                    },
                },
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            using var document = JsonDocument.Parse(body);
            JsonElement[] results = [.. document.RootElement.GetProperty("results").EnumerateArray()];
            AssertActive(results[0], member.Id);
            AssertError(results[1], "existing_user_has_no_credential");
            AssertError(results[2], "unknown_email");

            // Nothing was created and nothing was handed to the mail queue: the recording
            // dispatcher sees every hand-off, so this is not a race against the background sender.
            Assert.Equal(usersBefore, await CountUsersAsync(factory));
            Assert.Empty(dispatcher.Recipients());

            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext store = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            Assert.False(await store.Set<SingleUseCode>()
                .AnyAsync(code => code.Purpose == SingleUseCodePurpose.Invitation, TestContext.Current.CancellationToken));
        }

        /// <summary>Asserts an <c>Active</c> result naming the subject.</summary>
        private static void AssertActive(JsonElement result, string subject)
        {
            Assert.Equal("Active", result.GetProperty("status").GetString());
            Assert.Equal(subject, result.GetProperty("sub").GetString());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("error").ValueKind);
        }

        /// <summary>Asserts an error result with no status and no subject, and returns its error.</summary>
        private static string AssertError(JsonElement result, string? expected)
        {
            Assert.Equal(JsonValueKind.Null, result.GetProperty("status").ValueKind);
            Assert.Equal(JsonValueKind.Null, result.GetProperty("sub").ValueKind);
            string error = result.GetProperty("error").GetString()!;
            if (expected is not null)
            {
                Assert.Equal(expected, error);
            }

            return error;
        }

        /// <summary>Counts the users in the store.</summary>
        private static async Task<int> CountUsersAsync(StandaloneFactory factory)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext store = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            return await store.Users.CountAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Records every batch handed to the mail queue, then passes it on to the real
        ///     dispatcher so delivery still happens.
        /// </summary>
        private sealed class RecordingEmailDispatcher(IEmailDispatcher inner) : IEmailDispatcher
        {
            private readonly ConcurrentQueue<EmailMessage> _messages = new();

            /// <summary>Replaces the host's dispatcher with a recording one around it.</summary>
            public static RecordingEmailDispatcher Install(StandaloneFactory factory)
            {
                RecordingEmailDispatcher? recording = null;
                factory.ServiceOverrides.Add(services =>
                {
                    services.RemoveAll<IEmailDispatcher>();
                    services.AddSingleton<IEmailDispatcher>(provider =>
                        recording = new RecordingEmailDispatcher(provider.GetRequiredService<EmailDispatcher>()));
                });

                // The host resolves the dispatcher when it first needs it; resolve it now so the
                // test holds the instance the host uses.
                _ = factory.Services.GetRequiredService<IEmailDispatcher>();
                return recording!;
            }

            /// <summary>Every recipient queued so far, in queue order.</summary>
            public string[] Recipients()
            {
                return [.. _messages.SelectMany(static message => message.To).Select(static to => to.Address)];
            }

            /// <inheritdoc />
            public void Enqueue(IReadOnlyList<EmailMessage> messages)
            {
                foreach (EmailMessage message in messages)
                {
                    _messages.Enqueue(message);
                }

                inner.Enqueue(messages);
            }
        }
    }
}
