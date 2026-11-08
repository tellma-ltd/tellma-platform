// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     Marks a service type the platform resolves as a collection, such as
    ///     <see cref="IStartupCheck" />. Each feature registration of a marked type adds one
    ///     implementation, and no registration replaces another.
    /// </summary>
    /// <remarks>
    ///     Every other service type contributed by two features resolves to the registration of the
    ///     feature realised later, which is how a dependent feature replaces a service of a feature it
    ///     requires. Put this attribute on an extension point whose implementations are all consulted,
    ///     so one feature's contribution can never silently drop another's.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CollectionServiceAttribute : Attribute;
}
