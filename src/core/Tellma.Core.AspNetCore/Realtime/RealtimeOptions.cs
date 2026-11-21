// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     The realtime hub's options, bound from <c>Tellma:Realtime</c>: whether the host serves the
    ///     hub, the hub's keep-alive and client timeout, and how many instances the host expects to
    ///     run, which the hosting check compares with the hosting mode.
    /// </summary>
    public sealed class RealtimeOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Realtime";

        /// <summary>
        ///     Whether this host serves the hub; true by default. When false the hub is not mapped,
        ///     events are published to no one, and the client is told to poll.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>How often the server pings an idle connection; 15 seconds by default.</summary>
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        ///     How long the server waits for a message from a client before it closes the connection;
        ///     150 seconds by default, long enough for a hidden tab whose timers the browser throttles
        ///     to once a minute. At least twice <see cref="KeepAliveInterval" />.
        /// </summary>
        public TimeSpan ClientTimeoutInterval { get; set; } = TimeSpan.FromSeconds(150);

        /// <summary>
        ///     How many instances of the host the deployment runs; 1 by default. More than one without
        ///     a backplane or Azure SignalR Service is reported at startup, because an event published
        ///     on one instance would never reach another instance's connections.
        /// </summary>
        public int ExpectedInstances { get; set; } = 1;

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- The keep-alive: positive, or the server would never ping an idle connection ---
            if (KeepAliveInterval <= TimeSpan.Zero)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(KeepAliveInterval)} is {KeepAliveInterval}; it must be positive.",
                    [nameof(KeepAliveInterval)]);
            }

            // --- The client timeout: at least two pings, so one lost ping never closes a live connection ---
            if (ClientTimeoutInterval < KeepAliveInterval * 2)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(ClientTimeoutInterval)} is {ClientTimeoutInterval}; it must be at least twice {nameof(KeepAliveInterval)} ({KeepAliveInterval}).",
                    [nameof(ClientTimeoutInterval)]);
            }

            // --- The instances: one at least ---
            if (ExpectedInstances < 1)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(ExpectedInstances)} is {ExpectedInstances}; it must be at least 1.",
                    [nameof(ExpectedInstances)]);
            }
        }
    }
}
