// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     The error a job records when it fails or is retried: a code and the raw arguments its
    ///     message renders from, which the job's pages render from the string pack exactly as they
    ///     render a validation code.
    /// </summary>
    /// <remarks>
    ///     The code fits <see cref="JobLimits.MaxKeyLength" /> characters and the arguments, serialised
    ///     under the platform's JSON rules, <see cref="JobLimits.MaxArgumentsLength" />;
    ///     <see cref="JobItem{TItem}.Retry" />, <see cref="JobItem{TItem}.Fail" /> and
    ///     <see cref="JobFailedException" /> refuse a wider error with <see cref="ArgumentException" />.
    ///     A handler's codes live in its own assembly's string resources; the platform's <c>Jobs.*</c>
    ///     codes are Core's.
    /// </remarks>
    /// <param name="Code">The error's code, a dotted PascalCase resource key; not blank.</param>
    /// <param name="Arguments">The raw values its message renders from; empty when it has none.</param>
    public sealed record JobError(string Code, IReadOnlyDictionary<string, object?> Arguments)
        : CodedError(Code, Arguments)
    {
        /// <summary>Creates an error without arguments.</summary>
        /// <param name="code">The error's code, a dotted PascalCase resource key; not blank.</param>
        /// <exception cref="ArgumentException"><paramref name="code" /> is blank.</exception>
        public JobError(string code)
            : this(code, ReadOnlyDictionary<string, object?>.Empty)
        {
        }
    }
}
