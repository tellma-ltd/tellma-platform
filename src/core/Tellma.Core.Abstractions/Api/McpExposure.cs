// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     How the agent tools of the tenant MCP server see a stack or an action. It gates the tools
    ///     only: the web endpoints are projected whatever its value, and permissions are still
    ///     evaluated on every call.
    /// </summary>
    public enum McpExposure
    {
        /// <summary>Every tool applies: agents read, write and run actions under the caller's permissions.</summary>
        Full,

        /// <summary>
        ///     Agents read but never write: on a stack, the save and delete tools and every action that
        ///     is a mutation are refused; on an action, the action is refused when it is a mutation.
        /// </summary>
        ReadOnly,

        /// <summary>No tool reaches it: the stack or the action is absent from the agent tools.</summary>
        Hidden,
    }
}
