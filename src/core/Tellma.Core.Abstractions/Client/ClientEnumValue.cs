// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>One value of a named enum with its label: exactly one of a label key and a literal label.</summary>
    /// <param name="Value">The value as the wire spells it.</param>
    /// <param name="LabelKey">The resource key of the value's label; null, and omitted, for a literal label.</param>
    /// <param name="Label">A label that reads the same in every language, such as a zone id or a language's own name; null, and omitted, for a keyed label.</param>
    /// <param name="LabelFallback">The English a client shows when no resource declares <paramref name="LabelKey" />; null, and omitted, when one does or the label is literal.</param>
    public sealed record ClientEnumValue(string Value, string? LabelKey, string? Label, string? LabelFallback);
}
