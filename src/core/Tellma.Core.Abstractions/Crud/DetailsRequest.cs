// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     What a details read returns beside the rows: the extras it includes, by name. Each name must
    ///     be one of the stack's extras, at most the stack's extras ceiling of them, or the read is a
    ///     bad request.
    /// </summary>
    public sealed class DetailsRequest
    {
        /// <summary>No extras: the rows, their children and what they point at, as an import's hydration reads them.</summary>
        public static DetailsRequest None { get; } = new();

        /// <summary>The extras to include, by name; empty for none. A copy taken when set, so the shared <see cref="None" /> can never gain one.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public IReadOnlyList<string> Include
        {
            get;
            init => field = ReadOnlyCopies.Of(value ?? throw new ArgumentNullException(nameof(Include)));
        } = [];
    }
}
