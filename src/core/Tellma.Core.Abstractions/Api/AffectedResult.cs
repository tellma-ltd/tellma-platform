// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>The answer of a delete, and of an operation that reports only how many rows it affected.</summary>
    /// <param name="Count">The number of rows affected.</param>
    public sealed record AffectedResult(int Count);
}
