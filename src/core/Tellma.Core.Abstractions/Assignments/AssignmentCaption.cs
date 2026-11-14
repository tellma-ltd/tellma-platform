// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Assignments
{
    /// <summary>How an assignment reads in the assignee's list: the target's short reference, an amount, and detail arguments.</summary>
    /// <param name="Code">The target's short reference (<c>INV-0023</c>); null reads as <c>#</c> and the target's id.</param>
    /// <param name="Amount">An amount the caption shows; null for none.</param>
    /// <param name="Currency">The amount's ISO 4217 currency code; null for none.</param>
    /// <param name="Detail">The message arguments of the resource's detail line; null for none.</param>
    public sealed record AssignmentCaption(string? Code = null, decimal? Amount = null, string? Currency = null, object? Detail = null);
}
