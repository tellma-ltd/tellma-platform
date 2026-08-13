// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using MimeKit;
using SmtpServer;
using SmtpServer.Authentication;
using SmtpServer.ComponentModel;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace Tellma.Connector.Smtp.Adapter.Tests.Infrastructure
{
    /// <summary>
    ///     A real SMTP endpoint inside the test process: a real socket, real MIME on the wire, and no
    ///     external infrastructure.
    /// </summary>
    /// <remarks>
    ///     There is no canonical "the" SMTP server to run a live suite against, so this is the honest
    ///     equivalent — it exercises MailKit over a socket rather than mocking the client away.
    /// </remarks>
    public sealed class InProcessSmtpServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource _shutdown = new();
        private readonly SmtpServer.SmtpServer _server;
        private readonly Task _running;

        private InProcessSmtpServer(int port, ScriptedMessageStore store, ScriptedUserAuthenticator authenticator)
        {
            Port = port;
            Store = store;
            Authenticator = authenticator;

            ISmtpServerOptions options = new SmtpServerOptionsBuilder()
                .ServerName("localhost")
                .Endpoint(endpoint => endpoint
                    .Port(port)
                    .AllowUnsecureAuthentication(true))
                .Build();

            ServiceProvider services = new();
            services.Add(store);
            services.Add(authenticator);

            _server = new SmtpServer.SmtpServer(options, services);
            _running = _server.StartAsync(_shutdown.Token);
        }

        /// <summary>The loopback port the server listens on.</summary>
        public int Port { get; }

        /// <summary>The message store, which records what arrived and answers from a script.</summary>
        public ScriptedMessageStore Store { get; }

        /// <summary>The authenticator, which can be told to refuse.</summary>
        public ScriptedUserAuthenticator Authenticator { get; }

        /// <summary>Starts a server on a free loopback port.</summary>
        /// <returns>The running server.</returns>
        public static async Task<InProcessSmtpServer> StartAsync()
        {
            // Probe-then-bind on port 0, retried, because the whole solution's suites run in
            // parallel and a fixed port would collide. The listener is started on a background task,
            // so a bind failure surfaces as a readiness timeout rather than as a SocketException —
            // catching only SocketException here would leave the retry dead.
            List<Exception> failures = [];
            for (int attempt = 0; attempt < 5; attempt++)
            {
                int port = FreePort();
                InProcessSmtpServer server = new(port, new ScriptedMessageStore(), new ScriptedUserAuthenticator());
                try
                {
                    await server.WaitUntilListeningAsync();
                    return server;
                }
                catch (Exception failure) when (failure is SocketException or InvalidOperationException)
                {
                    // Another process took the port between the probe and the bind.
                    failures.Add(failure);
                    await server.DisposeAsync();
                }
            }

            throw new AggregateException(
                "The in-process SMTP server could not bind a free loopback port in five attempts.", failures);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _shutdown.CancelAsync();
            _server.Shutdown();

            try
            {
                await _running;
            }
            catch (OperationCanceledException)
            {
                // The expected way a cancelled listener finishes.
            }

            _shutdown.Dispose();
        }

        private static int FreePort()
        {
            TcpListener probe = new(IPAddress.Loopback, 0);
            probe.Start();
            try
            {
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        private async Task WaitUntilListeningAsync()
        {
            // Readiness by probe rather than by sleeping: the listener is up when it accepts.
            for (int attempt = 0; attempt < 100; attempt++)
            {
                // A listener that failed to bind faults its own task; surfacing that is faster and
                // far clearer than waiting out the full readiness budget.
                if (_running.IsFaulted)
                {
                    throw new InvalidOperationException(
                        $"The in-process SMTP server failed to start listening on port {Port}.", _running.Exception);
                }

                try
                {
                    using TcpClient client = new();
                    await client.ConnectAsync(IPAddress.Loopback, Port);
                    return;
                }
                catch (SocketException)
                {
                    await Task.Delay(20);
                }
            }

            throw new InvalidOperationException($"The in-process SMTP server never started listening on port {Port}.");
        }
    }

    /// <summary>Captures raw MIME and answers from a per-message script.</summary>
    public sealed class ScriptedMessageStore : MessageStore
    {
        private readonly Lock _gate = new();
        private readonly List<MimeMessage> _messages = [];
        private readonly Dictionary<string, SmtpResponse> _responses = new(StringComparer.Ordinal);

        /// <summary>Every message that reached the server, re-parsed from its wire bytes.</summary>
        public IReadOnlyList<MimeMessage> Messages
        {
            get
            {
                lock (_gate)
                {
                    return [.. _messages];
                }
            }
        }

        /// <summary>Answers a subject with a specific reply code.</summary>
        /// <param name="subject">The subject to match.</param>
        /// <param name="replyCode">The reply code to answer with.</param>
        /// <param name="message">The reply text.</param>
        public void RespondTo(string subject, int replyCode, string message)
        {
            lock (_gate)
            {
                _responses[subject] = new SmtpResponse((SmtpReplyCode)replyCode, message);
            }
        }

        /// <inheritdoc />
        public override async Task<SmtpResponse> SaveAsync(
            ISessionContext context,
            IMessageTransaction transaction,
            ReadOnlySequence<byte> buffer,
            CancellationToken cancellationToken)
        {
            using MemoryStream stream = new(buffer.ToArray());
            MimeMessage message = await MimeMessage.LoadAsync(stream, cancellationToken);

            lock (_gate)
            {
                _messages.Add(message);
                if (_responses.TryGetValue(message.Subject ?? string.Empty, out SmtpResponse? scripted))
                {
                    return scripted;
                }
            }

            return SmtpResponse.Ok;
        }
    }

    /// <summary>Accepts every credential unless told to refuse.</summary>
    public sealed class ScriptedUserAuthenticator : UserAuthenticator
    {
        /// <summary>When true, every AUTH attempt is refused.</summary>
        public bool RefuseEveryone { get; set; }

        /// <summary>The credentials the last successful attempt presented.</summary>
        public (string User, string Password)? LastCredentials { get; private set; }

        /// <inheritdoc />
        public override Task<bool> AuthenticateAsync(
            ISessionContext context, string user, string password, CancellationToken cancellationToken)
        {
            if (RefuseEveryone)
            {
                return Task.FromResult(false);
            }

            LastCredentials = (user, password);
            return Task.FromResult(true);
        }
    }
}
