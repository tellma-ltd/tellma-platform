// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;

namespace Tellma.Testing.Support.Options
{
    /// <summary>
    ///     An <see cref="IOptionsMonitor{TOptions}" /> over a value the test sets directly, for the
    ///     senders that read their options fresh per batch.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="value">The current value.</param>
    public sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        /// <inheritdoc />
        public TOptions CurrentValue { get; set; } = value;

        /// <inheritdoc />
        public TOptions Get(string? name)
        {
            return CurrentValue;
        }

        /// <inheritdoc />
        public IDisposable? OnChange(Action<TOptions, string?> listener)
        {
            // Nothing under test reacts to a change notification; the senders re-read CurrentValue.
            return null;
        }
    }
}
