// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Globalization;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §12.1: the drain must end before the host's shutdown timeout, which bounds the final
    // renewal of the handlers still running; only a host that runs the worker drains.
    /// <summary>Holds <see cref="JobsOptions.DrainTimeout" /> below the host's shutdown timeout when the worker runs.</summary>
    /// <param name="host">The host's options.</param>
    internal sealed class JobsHostValidation(IOptions<HostOptions> host) : IValidateOptions<JobsOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, JobsOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            TimeSpan shutdown = host.Value.ShutdownTimeout;
            return !options.Enabled || options.DrainTimeout < shutdown
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{JobsOptions.SectionPath}:{nameof(JobsOptions.DrainTimeout)} is {options.DrainTimeout:c}; it must be shorter than the host's shutdown timeout, {shutdown:c}, which the last lease renewal at shutdown is bound to."));
        }
    }
}
