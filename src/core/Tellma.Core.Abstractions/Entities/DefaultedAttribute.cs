// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Lets an editable property arrive empty: the service's preprocess hook fills it, and only then
    ///     is a required value checked. The property stays editable and its column keeps its
    ///     nullability.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class DefaultedAttribute : Attribute;
}
