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
    ///     logged and swallowed so one bad batch cannot stop the worker.
    /// </summary>
    /// <param name="queue">The dispatch queue.</param>
    /// <param name="scopeFactory">The scope factory for resolving the scoped sender.</param>
    /// <param name="logger">Delivery diagnostics.</param>
    public sealed class EmailDispatchHostedService(
        EmailDispatcher queue,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailDispatchHostedService> logger) : BackgroundService
    {
        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (IReadOnlyList<EmailMessage> batch in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    IEmailSender sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                    await sender.SendAsync(batch, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    EmailDispatchLog.DeliveryFailed(logger, exception, batch.Count);
                }
            }
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
