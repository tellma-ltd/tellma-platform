// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     Drains the <see cref="EmailDispatcher" /> and delivers each batch through a freshly
    ///     scoped <see cref="IEmailSender" /> (the SMTP sender is scoped). A delivery failure is
    ///     logged and swallowed so one bad batch cannot stop the worker. Shutdown closes the queue
    ///     only once the web server has finished with its requests, and the loop then delivers
    ///     what is left — bounded by the host's shutdown timeout — so a graceful restart does not
    ///     silently drop issued codes and links.
    /// </summary>
    /// <param name="queue">The dispatch queue.</param>
    /// <param name="scopeFactory">The scope factory for resolving the scoped sender.</param>
    /// <param name="logger">Delivery diagnostics.</param>
    public sealed class EmailDispatchHostedService(
        EmailDispatcher queue,
        IServiceScopeFactory scopeFactory,
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
                    await sender.SendAsync(batch, CancellationToken.None);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
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

        /// <summary>Nothing to do before the host starts.</summary>
        /// <param name="cancellationToken">Bounds the host's startup.</param>
        /// <returns>A completed task.</returns>
        public Task StartingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
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
