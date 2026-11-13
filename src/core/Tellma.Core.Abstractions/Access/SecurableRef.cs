// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     A securable named by its pair alone: the resource and the action a permission targets,
    ///     written <c>resource:action</c> in logs, diagnostics and the role editor.
    /// </summary>
    /// <param name="Resource">The resource, such as <c>gl.Center</c>.</param>
    /// <param name="Action">The action, such as <c>Save</c>.</param>
    public sealed record SecurableRef(string Resource, string Action)
    {
        /// <summary>The display form, <c>resource:action</c>.</summary>
        /// <returns>The pair as text.</returns>
        public override string ToString()
        {
            return $"{Resource}:{Action}";
        }
    }
}
