// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Calendars;
using Tellma.Core.Composition;
using Tellma.Core.Data.Batches;
using Tellma.Core.Notifications;
using Tellma.Core.Tests.Data.Batches;
using static Tellma.Core.Tests.Notifications.NotificationTestModel;

namespace Tellma.Core.Tests.Notifications
{
    /// <summary>
    ///     The delivery handler with no database: the one read round trip of every slice's load, the
    ///     resolution of each channel's recipients from the preference rows over each channel's default
    ///     in registration order, each recipient's negotiated localization, the request rebuilt from
    ///     the slice, and the single persist after every delivery, or none when a channel throws.
    /// </summary>
    public sealed class NotificationDeliveryHandlerTests : IDisposable
    {
        /// <summary>The time zone Ada prefers, three hours east of the tenant's UTC.</summary>
        private const string Riyadh = "Asia/Riyadh";

        /// <summary>The instant the fakes stamp.</summary>
        private static readonly DateTimeOffset Now = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

        /// <summary>The harness, composed with the catalogue's senders, so email and push have one each.</summary>
        private readonly BatchHarness _harness = new(compose: ComposeDelivering);

        /// <summary>Everything the fakes saw, in order.</summary>
        private readonly List<string> _log = [];

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        [Fact]
        public async Task Each_channel_lists_the_recipients_whose_row_else_default_is_on_in_slice_order_and_registration_order()
        {
            // --- Two slices: the first's users 3 and 5 opt into email and 3 mutes push; 9 is inactive ---
            Runner runner = new(_harness, ordinal => ordinal == 0
                ? [Users((3, "Ada", Riyadh), (5, "Ben", null)), Preferences((3, CoreFeature.EmailChannel, true), (5, CoreFeature.EmailChannel, true), (3, Push, false), (3, "gone", true))]
                : [Users((3, "Ada", Riyadh)), Preferences()]);
            Fixture fixture = Create(runner);
            JobBatch<Job> batch = Batch(
                new NotificationDeliveryArguments(Shipped, NotificationSeverity.Success, /*lang=json,strict*/ """{"order":"SO-1"}""", OrderResource, 41, 2, [5, 3, 9]),
                new NotificationDeliveryArguments(Held, NotificationSeverity.Warning, null, null, null, null, [3]));

            await fixture.Handler.ExecuteAsync(batch);

            // --- One read round trip with one statement per slice, then the deliveries, then one persist ---
            Assert.Equal(1, runner.Runs);
            Assert.Equal(BatchPurpose.Read, runner.Purpose);
            Assert.Equal(2, runner.Statements);
            Assert.Equal(
                [
                    $"{CoreFeature.EmailChannel} {Shipped} 5,3",
                    $"{Push} {Shipped} 5",
                    $"{Push} {Held} 3",
                    "persist",
                ],
                _log);
            Assert.Equal(1, fixture.Host.Persists);
        }

        [Fact]
        public async Task The_request_is_rebuilt_from_the_slice_and_each_recipient_carries_their_negotiated_context()
        {
            Runner runner = new(_harness, _ => [Users((3, "Ada", Riyadh), (5, "Ben", null)), Preferences()]);
            Fixture fixture = Create(runner);
            JobBatch<Job> batch = Batch(new NotificationDeliveryArguments(Shipped, NotificationSeverity.Error, /*lang=json,strict*/ """{"order":"SO-1"}""", OrderResource, 41, 2, [5, 3]));

            await fixture.Handler.ExecuteAsync(batch);

            // --- Push alone delivers (email is off by default), with the slice's request ---
            (NotificationRequest request, IReadOnlyList<NotificationRecipient> recipients) = Assert.Single(fixture.Push.Deliveries);
            Assert.Empty(fixture.Email.Deliveries);
            Assert.Equal(Shipped, request.Type);
            Assert.Equal([5, 3], request.RecipientIds);
            Assert.Equal(NotificationSeverity.Error, request.Severity);
            Assert.Equal("SO-1", Assert.IsType<JsonElement>(request.Arguments).GetProperty("order").GetString());
            Assert.Equal(OrderResource, request.TargetResource);
            Assert.Equal(41, request.TargetId);
            Assert.Equal(2, request.ActorUserId);
            Assert.Null(request.DedupKey);

            // --- Each recipient negotiated from their own preferences, the tenant's settings and no headers ---
            Assert.Equal([5, 3], recipients.Select(static recipient => recipient.UserId));
            NegotiationInput ada = Assert.Single(fixture.Negotiator.Inputs, static input => input.PreferredTimeZone == Riyadh);
            Assert.Null(ada.AcceptLanguage);
            Assert.Null(ada.CalendarHeader);
            Assert.Null(ada.TimeZoneHeader);
            Assert.Null(ada.MessageLanguage);
            Assert.Same(fixture.Accessor.Current.TenantSettings, ada.Settings);
            Assert.Equal(TimeSpan.FromHours(3), recipients[1].Localization.TimeZone.BaseUtcOffset);
            Assert.Equal(TimeSpan.Zero, recipients[0].Localization.TimeZone.BaseUtcOffset);
            Assert.Equal("Ada", recipients[1].Name);
            Assert.Equal("ada@example.com", recipients[1].ContactEmail);
        }

        [Fact]
        public async Task A_slice_without_active_recipients_delivers_nothing_and_still_persists_once()
        {
            Runner runner = new(_harness, _ => [Users(), Preferences()]);
            Fixture fixture = Create(runner);

            await fixture.Handler.ExecuteAsync(Batch(new NotificationDeliveryArguments(Shipped, NotificationSeverity.Info, null, null, null, null, [9])));

            Assert.Empty(fixture.Push.Deliveries);
            Assert.Equal(["persist"], _log);
        }

        [Fact]
        public async Task A_channel_that_throws_ends_the_invocation_with_nothing_persisted()
        {
            Runner runner = new(_harness, _ => [Users((5, "Ben", null)), Preferences((5, CoreFeature.EmailChannel, true))]);
            Fixture fixture = Create(runner, pushFailure: new InvalidOperationException("The push transport is down."));

            InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.Handler.ExecuteAsync(Batch(new NotificationDeliveryArguments(Shipped, NotificationSeverity.Info, null, null, null, null, [5]))));

            Assert.Equal("The push transport is down.", thrown.Message);
            Assert.Equal([$"{CoreFeature.EmailChannel} {Shipped} 5"], _log);
            Assert.Equal(0, fixture.Host.Persists);
        }

        [Fact]
        public void The_handler_declares_the_delivery_attributes()
        {
            JobHandlerAttribute declared = Assert.Single(typeof(NotificationDeliveryHandler).GetCustomAttributes(typeof(JobHandlerAttribute), inherit: false).Cast<JobHandlerAttribute>());

            Assert.Equal("core.notification-delivery", declared.Key);
            Assert.Equal(4, declared.BatchSize);
            Assert.Equal(12, declared.MaxAttempts);
            Assert.True(declared.InboxOnlyFailureNotices);
            Assert.True(declared.RunsWhenReadOnly);
        }

        /// <summary>The handler over the fakes.</summary>
        /// <param name="runner">The read runner.</param>
        /// <param name="pushFailure">What the push sender throws; null to deliver.</param>
        /// <returns>The handler with its fakes.</returns>
        private Fixture Create(Runner runner, Exception? pushFailure = null)
        {
            RecordingSender email = new(CoreFeature.EmailChannel, _log, null);
            RecordingSender push = new(Push, _log, pushFailure);
            RecordingNegotiator negotiator = new(_harness.Services.GetRequiredService<ILocalizationNegotiator>());
            Accessor accessor = new();
            CountingHost host = new(_log);
            NotificationDeliveryHandler handler = new(
                runner,
                _harness.Services.GetRequiredService<NotificationDeliveryChannels>(),
                [push, email],
                negotiator,
                accessor,
                new RefusedSettings(),
                host);
            return new Fixture(handler, email, push, negotiator, accessor, host);
        }

        /// <summary>A batch of jobs, one per slice.</summary>
        /// <param name="slices">The slices.</param>
        /// <returns>The batch.</returns>
        private JobBatch<Job> Batch(params NotificationDeliveryArguments[] slices)
        {
            return new JobBatch<Job>
            {
                Items = [.. slices.Select((slice, index) =>
                {
                    Job job = new()
                    {
                        Id = index + 1,
                        HandlerKey = "core.notification-delivery",
                        ArgumentsJson = JsonSerializer.Serialize(slice, TellmaJson.Options),
                    };
                    return new JobItem<Job> { Job = job, Item = job, Progress = new SilentProgress() };
                })],
                Batch = _harness.Batch(BatchPurpose.Persist),
            };
        }

        /// <summary>The load's users set.</summary>
        /// <param name="rows">The users: id, name and preferred time zone.</param>
        /// <returns>The set.</returns>
        private static DataTable Users(params (int Id, string Name, string? TimeZone)[] rows)
        {
            DataTable table = new();
            foreach (string column in new[] { "Id", "Name", "Email", "ContactEmail", "ContactMobile", "PreferredLanguage", "PreferredCalendar", "PreferredTimeZone", "PreferredRegion" })
            {
                _ = table.Columns.Add(column, column == "Id" ? typeof(int) : typeof(string));
            }

            foreach ((int id, string name, string? timeZone) in rows)
            {
                string lower = name.ToLowerInvariant();
                _ = table.Rows.Add(id, name, $"{lower}@sign.in", $"{lower}@example.com", DBNull.Value, DBNull.Value, DBNull.Value, (object?)timeZone ?? DBNull.Value, DBNull.Value);
            }

            return table;
        }

        /// <summary>The load's preferences set.</summary>
        /// <param name="rows">The rows: user, channel, enabled.</param>
        /// <returns>The set.</returns>
        private static DataTable Preferences(params (int UserId, string Channel, bool Enabled)[] rows)
        {
            DataTable table = new();
            _ = table.Columns.Add("UserId", typeof(int));
            _ = table.Columns.Add("Channel", typeof(string));
            _ = table.Columns.Add("Enabled", typeof(bool));
            foreach ((int userId, string channel, bool enabled) in rows)
            {
                _ = table.Rows.Add(userId, channel, enabled);
            }

            return table;
        }

        /// <summary>The handler and the fakes a test inspects.</summary>
        /// <param name="Handler">The handler.</param>
        /// <param name="Email">The email sender.</param>
        /// <param name="Push">The push sender.</param>
        /// <param name="Negotiator">The negotiator.</param>
        /// <param name="Accessor">The scope's context.</param>
        /// <param name="Host">The job frame's write host.</param>
        private sealed record Fixture(NotificationDeliveryHandler Handler, RecordingSender Email, RecordingSender Push, RecordingNegotiator Negotiator, Accessor Accessor, CountingHost Host);

        /// <summary>A runner that composes on the harness and replays each statement's sets.</summary>
        /// <param name="harness">The harness.</param>
        /// <param name="sets">The sets of the statement at an ordinal.</param>
        private sealed class Runner(BatchHarness harness, Func<int, DataTable[]> sets) : IGuardedBatchRunner
        {
            /// <summary>How many round trips ran.</summary>
            public int Runs { get; private set; }

            /// <summary>The last round trip's purpose.</summary>
            public BatchPurpose? Purpose { get; private set; }

            /// <summary>The last round trip's statement count.</summary>
            public int Statements { get; private set; }

            /// <inheritdoc />
            public async Task<TResult> RunAsync<TResult>(
                BatchPurpose purpose,
                Func<ConnectedUser, IDataBatch, ValueTask> compose,
                Func<ConnectedUser, BatchOutcome, TResult> read,
                CancellationToken cancellationToken = default)
            {
                Runs++;
                Purpose = purpose;
                ConnectedUser user = Connected();
                DataBatch batch = harness.Batch(purpose);
                await compose(user, batch);
                Statements = batch.Statements.Count;

                // --- Every statement's sets in one reader, read in statement order ---
                DataTable[] all = [.. Enumerable.Range(0, batch.Statements.Count).SelectMany(sets)];
                using DataTableReader reader = new(all);
                ResultSetCursor cursor = new(reader);
                foreach (BatchStatement statement in batch.Statements)
                {
                    await statement.Reader!(cursor, cancellationToken);
                }

                cursor.ApplyCompletions();
                return read(user, new BatchOutcome(new VersionTagSnapshot(1, new Dictionary<string, VersionTag>(), Now), null, 1));
            }

            /// <summary>The system user, as a job's scope connects it.</summary>
            /// <returns>The user.</returns>
            private static ConnectedUser Connected()
            {
                return new ConnectedUser(
                    1,
                    UserKind.Individual,
                    UserState.Joined,
                    new UserProfile("System", null, null, null, null, null, null, null, null, null, null, null, null, null, Now),
                    Guid.Empty,
                    new UserAccess(),
                    new Dictionary<string, Guid>(StringComparer.Ordinal),
                    null);
            }
        }

        /// <summary>A sender that records each delivery to the shared log, or throws.</summary>
        /// <param name="key">The channel's key.</param>
        /// <param name="log">The shared log.</param>
        /// <param name="failure">What every delivery throws; null to record.</param>
        private sealed class RecordingSender(string key, List<string> log, Exception? failure) : INotificationChannel
        {
            /// <summary>The deliveries, in order.</summary>
            public List<(NotificationRequest Request, IReadOnlyList<NotificationRecipient> Recipients)> Deliveries { get; } = [];

            /// <inheritdoc />
            public string Key => key;

            /// <inheritdoc />
            public Task DeliverAsync(IOpenWriteHost host, NotificationRequest request, IReadOnlyList<NotificationRecipient> recipients, CancellationToken cancellationToken = default)
            {
                if (failure is not null)
                {
                    throw failure;
                }

                Deliveries.Add((request, recipients));
                log.Add($"{key} {request.Type} {string.Join(',', recipients.Select(static recipient => recipient.UserId))}");
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public Task<TestNotificationResult> SendTestAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>The platform's negotiator, recording every input.</summary>
        /// <param name="inner">The platform's negotiator.</param>
        private sealed class RecordingNegotiator(ILocalizationNegotiator inner) : ILocalizationNegotiator
        {
            /// <summary>The inputs, in order.</summary>
            public List<NegotiationInput> Inputs { get; } = [];

            /// <inheritdoc />
            public LocalizationContext Negotiate(NegotiationInput input)
            {
                Inputs.Add(input);
                return inner.Negotiate(input);
            }
        }

        /// <summary>A job scope's context: a tenant offering English and Arabic, with its settings.</summary>
        private sealed class Accessor : IRequestContextAccessor
        {
            /// <inheritdoc />
            public RequestContext Current { get; } = RequestContext.Tenantless(new GregorianCalendarSystem(), Now) with
            {
                Tenant = new TenantDescriptor(1, "Acme", null, null, ["en", "ar"], TenantCategory.Live, TenantState.Active, null),
                UserId = 1,
                TenantSettings = new TenantSettings
                {
                    TenantId = 1,
                    Tag = new VersionTag(Guid.Parse("00000000-0000-0000-0000-00000000000a")),
                    Names = ["Acme", null, null],
                    Languages = [new LanguageInfo("en", "English", "E", false), new LanguageInfo("ar", "العربية", "ع", true)],
                    Shape = MultilingualShape.Primary,
                    Calendars = [],
                    TimeZone = TimeZoneInfo.Utc,
                    SqlServerTimeZoneName = "UTC",
                    ModifiedAt = Now,
                    Entries = new Dictionary<string, JsonElement>(),
                },
            };
        }

        /// <summary>A settings cache the handler must not need, since the scope's context holds the settings.</summary>
        private sealed class RefusedSettings : ITenantSettingsCache
        {
            /// <inheritdoc />
            public Task<CacheResult<TenantSettings>> GetAsync()
            {
                throw new InvalidOperationException("The scope's context holds the settings.");
            }

            /// <inheritdoc />
            public Task<(byte[] Json, string WireTag)> GetForClientAsync()
            {
                throw new InvalidOperationException("The handler never serves the client.");
            }

            /// <inheritdoc />
            public TenantSettings? Peek()
            {
                throw new InvalidOperationException("The scope's context holds the settings.");
            }
        }

        /// <summary>A job frame's write host that counts its persists.</summary>
        /// <param name="log">The shared log.</param>
        private sealed class CountingHost(List<string> log) : IOpenWriteHost
        {
            /// <summary>How many persists ran.</summary>
            public int Persists { get; private set; }

            /// <inheritdoc />
            public WriteHostKind Kind => WriteHostKind.Job;

            /// <inheritdoc />
            public string Operation => "core.notification-delivery";

            /// <inheritdoc />
            public IConfirmations Confirmations => throw new NotSupportedException();

            /// <inheritdoc />
            public Task<EnlistmentOutcome> PersistAsync(CancellationToken cancellationToken = default)
            {
                Persists++;
                log.Add("persist");
                return Task.FromResult(new EnlistmentOutcome(Now, new VersionTagSnapshot(1, new Dictionary<string, VersionTag>(), Now), null));
            }
        }

        /// <summary>Progress that goes nowhere.</summary>
        private sealed class SilentProgress : IJobProgress
        {
            /// <inheritdoc />
            public void Report(byte? percent, JobMessage? message, object? state)
            {
            }

            /// <inheritdoc />
            public Task FlushAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public void Append(IDataBatch batch, byte? percent, JobMessage? message, object? state)
            {
            }
        }
    }
}
