// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Declares that a writer (a service, a validator component, a stack companion, a job handler
    ///     or a provisioning step) writes rows of <typeparamref name="TTarget" />'s stack by enlisting
    ///     them with its frame. The enlistment methods take it as their receiver, so an undeclared pair
    ///     is a compile error; the target's stack must also accept enlisted writes.
    /// </summary>
    /// <typeparam name="TTarget">The target stack's entity type.</typeparam>
    [SuppressMessage(
        "Design",
        "CA1040:Avoid empty interfaces",
        Justification = "A declaration read from the type: the registry reads every pair from it, and the enlistment methods' receiver constraint makes an undeclared pair a compile error.")]
    public interface IEnlists<TTarget>;
}
