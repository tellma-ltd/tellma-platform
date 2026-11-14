// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Identity;

namespace Tellma.Core.Tests.Identity
{
    /// <summary>
    ///     An <see cref="IdentityServerClient" /> over a <see cref="FakeIdentityServer" />, on a clock the
    ///     suite moves, with its instruments and its logs (Trace included) collected.
    /// </summary>
    internal sealed class IdentityClientHarness : IDisposable
    {
        /// <summary>The service client's secret; never in a log.</summary>
        public const string Secret = "svc-secret-5d1f0c";

        /// <summary>The distribution's public origin.</summary>
        public const string PublicOrigin = "https://acme.app.tellma.com";

        /// <summary>The instant the clock starts at.</summary>
        public static readonly DateTimeOffset Start = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        /// <summary>The identity server's issuer: the in-proc engine's, whose path the routes keep.</summary>
        public static readonly Uri Authority = new(PublicOrigin + "/id");

        /// <summary>The meter factory and the logs.</summary>
        private readonly ServiceProvider _services = new ServiceCollection()
            .AddMetrics()
            .AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Trace))
            .AddFakeLogging()
            .BuildServiceProvider();

        /// <summary>Creates the client.</summary>
        /// <param name="expiresIn">The lifetime of the tokens the default handler issues, in seconds; none when null.</param>
        public IdentityClientHarness(long? expiresIn = 3600)
        {
            // The options as the platform settles them after binding: the client id and the audience.
            IdentityServerClientOptions options = new() { Authority = Authority, ServiceClientSecret = Secret };
            IdentityComposition.Settle(options, "acme", new ConfigurationBuilder().AddInMemoryCollection([new("Tellma:PublicOrigin", PublicOrigin)]).Build());

            Server.Token = (_, number, _) => Task.FromResult(FakeIdentityServer.TokenAnswer("token-" + number, expiresIn));
            Client = new IdentityServerClient(
                Server,
                Options.Create(options),
                Time,
                _services.GetRequiredService<IMeterFactory>(),
                _services.GetRequiredService<ILogger<IdentityServerClient>>());
        }

        /// <summary>The clock.</summary>
        public FakeTimeProvider Time { get; } = new(Start);

        /// <summary>The fake identity server.</summary>
        public FakeIdentityServer Server { get; } = new();

        /// <summary>The client under test.</summary>
        public IdentityServerClient Client { get; }

        /// <summary>Every log record written so far.</summary>
        public IReadOnlyList<FakeLogRecord> Logs => _services.GetFakeLogCollector().GetSnapshot();

        /// <summary>The <c>IdentityCall</c> records written so far.</summary>
        public List<FakeLogRecord> Calls => [.. Logs.Where(static record => record.Id.Name == "IdentityCall")];

        /// <summary>Collects one of the client's instruments.</summary>
        /// <typeparam name="T">The instrument's value type.</typeparam>
        /// <param name="instrument">The instrument's name.</param>
        /// <returns>The collector.</returns>
        public MetricCollector<T> Collector<T>(string instrument)
            where T : struct
        {
            return new MetricCollector<T>(_services.GetRequiredService<IMeterFactory>(), UsersTelemetryNames.MeterName, instrument);
        }

        /// <summary>Invitations to <c>user-0@acme.com</c>, <c>user-1@acme.com</c>, ….</summary>
        /// <param name="count">How many.</param>
        /// <returns>The invitations.</returns>
        public static List<IdentityInvitation> Invitations(int count)
        {
            return [.. Enumerable.Range(0, count).Select(static i => new IdentityInvitation($"user-{i}@acme.com", null, null, null, null))];
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
            _services.Dispose();
        }
    }

    /// <summary>
    ///     Records the activities the client starts under one root activity of the test's own, so
    ///     activities of suites that run alongside are never seen.
    /// </summary>
    internal sealed class ActivityRecorder : IDisposable
    {
        /// <summary>The test's root activity.</summary>
        private readonly Activity _root;

        /// <summary>The listener.</summary>
        private readonly ActivityListener _listener;

        /// <summary>Starts the root activity, current for the rest of the test, and the listener.</summary>
        public ActivityRecorder()
        {
            _root = new Activity("identity-client-test").Start();
            ActivityTraceId trace = _root.TraceId;
            _listener = new ActivityListener
            {
                // By name: the source may be created after the listener, when comparing with the
                // static field that is about to hold it would find it still unset.
                ShouldListenTo = static source => source.Name == DataTelemetryNames.ActivitySourceName,
                Sample = (ref options) =>
                    options.Parent.TraceId == trace ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
                ActivityStopped = activity =>
                {
                    if (activity.TraceId == trace)
                    {
                        Stopped.Enqueue(activity);
                        OnStopped?.Invoke(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        /// <summary>The activities stopped, in order.</summary>
        public ConcurrentQueue<Activity> Stopped { get; } = new();

        /// <summary>Runs when an activity stops, on the thread that stopped it.</summary>
        public Action<Activity>? OnStopped { get; set; }

        /// <inheritdoc />
        public void Dispose()
        {
            _listener.Dispose();
            _root.Stop();
        }
    }
}
