// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Marks the one kind of type allowed to construct table-valued-parameter records and address
    ///     their fields by ordinal: the platform's table-type binder, which derives every ordinal from
    ///     the model's table-type definitions. The analyzers refuse ordinal record binding in any other
    ///     type.
    /// </summary>
    /// <remarks>Not inherited: each binding type is marked where it is declared.</remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class TableTypeBinderAttribute : Attribute;
}
