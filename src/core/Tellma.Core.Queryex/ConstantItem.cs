// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     One expression of a constant statement, and the type its value has to have.
    /// </summary>
    /// <remarks>
    ///     The expression reads no row: it may not name a path, an aggregation, or a parameter, and
    ///     it is a single expression rather than a list. A written string literal is read at
    ///     <paramref name="Type" /> wherever the language reads one beside a column of that type;
    ///     any other expression has to be of that type already.
    /// </remarks>
    /// <param name="Text">The expression text.</param>
    /// <param name="Type">The type the expression has to have, and the type of its result column.</param>
    public sealed record ConstantItem(string Text, QueryexType Type);
}
