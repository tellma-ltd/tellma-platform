// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Tellma.Core.Testing.Diagnostics
{
    /// <summary>Wires <see cref="TestOutputLoggerProvider" /> into a logging builder.</summary>
    public static class TestOutputLoggingExtensions
    {
        /// <summary>Sends every log record to the running test's output.</summary>
        /// <remarks>
        ///     The minimum level stays the caller's decision, because it is a per-suite judgment: a
        ///     transport that logs a provider refusal at <see cref="LogLevel.Debug" /> needs the floor
        ///     lowered before its most useful line is emitted at all.
        /// </remarks>
        /// <param name="builder">The logging builder.</param>
        /// <returns>The builder, for chaining.</returns>
        public static ILoggingBuilder AddTestOutput(this ILoggingBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // TryAddEnumerable with an implementation type, so composing twice — a suite that calls
            // this and a helper that also does — does not double every line in the output.
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<ILoggerProvider, TestOutputLoggerProvider>());

            return builder;
        }
    }
}
