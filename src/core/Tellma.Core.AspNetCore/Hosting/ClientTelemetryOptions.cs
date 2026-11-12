// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>The client telemetry relay, bound from <c>Tellma:ClientTelemetry</c>.</summary>
    public sealed class ClientTelemetryOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:ClientTelemetry";

        /// <summary>Whether the client reports telemetry; on by default.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>The share of sessions that report, between 0 and 1; 1 by default.</summary>
        [Range(0.0, 1.0)]
        public double SampleRate { get; set; } = 1.0;
    }
}
