// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>The kind of frame the platform opened around a piece of work that writes.</summary>
    public enum WriteHostKind
    {
        /// <summary>The service pipeline around a save, a delete or an entity action; never persisted by its participants.</summary>
        Pipeline,

        /// <summary>An API action, which persists its frame itself.</summary>
        ApiAction,

        /// <summary>A background job's handler, whose frame commits with the job's completion.</summary>
        Job,

        /// <summary>A provisioning step, under the step's system scope.</summary>
        Provisioning,
    }
}
