// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     One validation failure: the value it concerns, as a path into the payload, with a code and
    ///     the raw arguments its message renders from. A <see cref="ValidationException" /> carries
    ///     every failure an operation found.
    /// </summary>
    /// <remarks>
    ///     The path is a list of segments and is never parsed: the web edge renders property segments
    ///     through its JSON naming policy (<c>entities[0].roleMemberships[2].roleId</c>) and the Excel
    ///     codec maps segments to cells. A whole-payload failure has <see cref="ValidationPath.Root" />.
    /// </remarks>
    /// <param name="Path">The value the failure concerns.</param>
    /// <param name="Code">The failure's code, a dotted PascalCase resource key.</param>
    /// <param name="Arguments">The raw values its message renders from; empty when it has none.</param>
    public sealed record ValidationError(ValidationPath Path, string Code, IReadOnlyDictionary<string, object?> Arguments)
        : CodedError(Code, Arguments)
    {
        /// <inheritdoc cref="ValidationError(ValidationPath, string, IReadOnlyDictionary{string, object?})" />
        public ValidationPath Path { get; } = Path ?? throw new ArgumentNullException(nameof(Path));
    }
}
