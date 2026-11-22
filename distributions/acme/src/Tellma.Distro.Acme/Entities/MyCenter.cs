// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Entities;
using Tellma.Module.Gl;

namespace Tellma.Distro.Acme.Entities
{
    // Spec 0018 §1.4: a leaf extending a pack entity. The composition registers it in place of
    // Center, so gl.Centers gains the column, its foreign key FK_Centers_ManagerId to core.Users and
    // its index, and the platform closes the centers' service and sample step over it.
    /// <summary>Acme's responsibility center: GL's center with the user who manages it.</summary>
    public sealed class MyCenter : Center
    {
        /// <summary>The user who manages the center; null for none.</summary>
        [References(typeof(User))]
        public int? ManagerId { get; set; }
    }
}
