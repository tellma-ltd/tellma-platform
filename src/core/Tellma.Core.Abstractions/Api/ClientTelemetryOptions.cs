// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The client telemetry relay, bound from <c>Tellma:ClientTelemetry</c>. Both values reach the
    ///     SPA through the distribution's information document, so changing one needs no client
    ///     rebuild.
    /// </summary>
    public sealed class ClientTelemetryOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:ClientTelemetry";

        /// <summary>
        ///     Whether the client reports telemetry; on by default. Off, the client queues nothing, and
        ///     the relay answers every batch it is sent <c>204</c> and discards it.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        ///     The share of browser tabs that report their sampled items, between 0 and 1; 1 by default.
        ///     Errors, and every item not sampled by default, such as a slow request, are always reported.
        /// </summary>
        [Range(0.0, 1.0)]
        public double SampleRate { get; set; } = 1.0;
    }
}
