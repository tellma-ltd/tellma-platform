// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     Drains the <see cref="EmailDispatcher" /> and delivers each batch through a freshly
    ///     scoped <see cref="IEmailSender" /> — the platform's router is registered scoped, so it
    ///     cannot be captured once and reused across batches. A delivery failure is logged and
    ///     swallowed so one bad batch cannot stop the worker. Shutdown closes the queue only once
    ///     the web server has finished with its requests, and the loop then delivers what is left
    ///     — bounded by the host's shutdown timeout — so a graceful restart does not silently drop
    ///     issued codes and links.
    /// </summary>
    /// <param name="queue">The dispatch queue.</param>
    /// <param name="scopeFactory">The scope factory for resolving the scoped sender.</param>
    /// <param name="isRegistered">Container probe, to fail a host that composed no transport.</param>
    /// <param name="logger">Delivery diagnostics.</param>
    public sealed class EmailDispatchHostedService(
        EmailDispatcher queue,
        IServiceScopeFactory scopeFactory,
        IServiceProviderIsService isRegistered,
        ILogger<EmailDispatchHostedService> logger) : BackgroundService, IHostedLifecycleService
    {
        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // The read is deliberately not cancelled by the stop signal: the queue is closed in
            // StoppedAsync instead, and ReadAllAsync then ends by itself once the remaining
            // batches are drained.
            await foreach (IReadOnlyList<EmailMessage> batch in queue.Reader.ReadAllAsync(CancellationToken.None))
            {
                try
                {
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    IEmailSender sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

                    // The results are deliberately not inspected. Each one is already counted and
                    // logged centrally by the pipeline, and there is nothing this worker could do
                    // with a per-message outcome: a code or link that failed to go out is recovered
                    // by the user asking for another, not by anything here. That includes the
                    // outcomes that are not failures at all — Sandboxed cannot occur while this
                    // deployment declares itself tenant-less, and Rejected means the message was
                    // malformed, which retrying would not mend.
                    await sender.SendAsync(batch, CancellationToken.None);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The contract lets a sender throw only when nothing went out, so a whole batch
                    // is lost here rather than half-delivered — except for a transport that breaks
                    // the contract, which the pipeline surfaces as InvalidOperationException after
                    // mail may already have gone. Neither is retried: see above.
                    EmailDispatchLog.DeliveryFailed(logger, exception, batch.Count);
                }
            }
        }

        /// <summary>
        ///     Returns immediately rather than waiting for the drain. Hosted services stop in
        ///     reverse registration order and this one is registered after the web host's, so its
        ///     stop signal arrives while Kestrel is still completing in-flight requests: blocking
        ///     here would hold up the very shutdown step that has to happen before the queue can
        ///     safely close.
        /// </summary>
        /// <param name="cancellationToken">Bounds the host's shutdown.</param>
        /// <returns>A completed task.</returns>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Refuses to start a host that has nothing to send mail with.
        ///     <para>
        ///         The engine registers no transport — that is the host's composition — and it
        ///         resolves the sender per batch, on a background worker whose failures are caught
        ///         and logged. Those two together are what make the omission so quiet: a host that
        ///         never composed the email pipeline boots green, serves sign-in normally, and
        ///         drops every code and invitation link into a warning nobody is reading. The
        ///         engine used to fail startup when no transport was configured; that rule now
        ///         belongs to the pipeline's own validator, which a host that skipped the pipeline
        ///         does not have either. This is the seam where that gap closes.
        ///     </para>
        /// </summary>
        /// <param name="cancellationToken">Bounds the host's startup.</param>
        /// <returns>A completed task once the composition is known to be serviceable.</returns>
        public Task StartingAsync(CancellationToken cancellationToken)
        {
            // Asking the container rather than resolving: IEmailSender is scoped, and the root
            // provider cannot resolve a scoped service — the question here is only whether a host
            // supplied one at all.
            return isRegistered.IsService(typeof(IEmailSender))
                ? Task.CompletedTask
                : throw new InvalidOperationException(
                    "No email transport is registered, so invitations, sign-in codes and password "
                    + "resets would be accepted and silently discarded. Compose the platform's "
                    + "email pipeline in the host — AddTellmaEmail() plus a transport — or, in-proc, "
                    + "confirm the hosting distribution does.");
        }

        /// <summary>Nothing to do once the host has started.</summary>
        /// <param name="cancellationToken">Bounds the host's startup.</param>
        /// <returns>A completed task.</returns>
        public Task StartedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>Nothing to do before the host stops.</summary>
        /// <param name="cancellationToken">Bounds the host's shutdown.</param>
        /// <returns>A completed task.</returns>
        public Task StoppingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Closes the queue and waits for the drain. This runs after every hosted service's
        ///     <c>StopAsync</c>, the web host's included, so the server has finished its in-flight
        ///     requests and no further mail can be enqueued. Closing on the stop signal instead
        ///     would shut the queue while requests were still completing, and the codes and links
        ///     they issue would be written to a closed queue and dropped.
        /// </summary>
        /// <param name="cancellationToken">Bounds the host's shutdown.</param>
        /// <returns>A task that completes when the queue has drained.</returns>
        public async Task StoppedAsync(CancellationToken cancellationToken)
        {
            queue.Complete();

            if (ExecuteTask is { } drain)
            {
                await drain;
            }
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            // A host torn down without a graceful stop never reaches StoppedAsync, which would
            // leave the loop parked on a queue nothing will ever close. Nothing can be serving
            // requests by now, so closing it here is safe and releases the loop.
            queue.Complete();
            base.Dispose();
        }
    }

    /// <summary>Source-generated log messages for <see cref="EmailDispatchHostedService" />.</summary>
    internal static partial class EmailDispatchLog
    {
        /// <summary>A background email batch failed to send.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The delivery failure.</param>
        /// <param name="count">The number of messages in the failed batch.</param>
        [LoggerMessage(Level = LogLevel.Warning, Message = "Background delivery of {Count} email(s) failed.")]
        public static partial void DeliveryFailed(ILogger logger, Exception exception, int count);
    }
}
