// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     What an action without a result of its own answers beside the target ids. An action with its
    ///     own result runs no read-back and ignores both.
    /// </summary>
    /// <param name="ReturnEntities">Whether the target rows are read back in details shape after the action; default false.</param>
    /// <param name="Details">The extras the read-back includes; null for none.</param>
    public sealed record ActionOptions(bool ReturnEntities = false, DetailsRequest? Details = null);
}
