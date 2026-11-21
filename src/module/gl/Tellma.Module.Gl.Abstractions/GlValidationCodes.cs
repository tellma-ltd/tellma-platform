// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Module.Gl
{
    /// <summary>The validation codes the GL module's stacks report, each a dotted resource key.</summary>
    public static class GlValidationCodes
    {
        /// <summary>
        ///     A center's parent is not a grouping center (<see cref="CenterType.Abstract" /> or
        ///     <see cref="CenterType.BusinessUnit" />): reported at the row's parent, or at its type when
        ///     the row itself stops grouping while it still has children.
        /// </summary>
        public const string ParentMustBeGrouping = "Gl.Centers.ParentMustBeGrouping";

        /// <summary>A deleted center still has children the delete leaves behind; reported at the center's id.</summary>
        public const string HasChildren = "Gl.Centers.HasChildren";
    }
}
