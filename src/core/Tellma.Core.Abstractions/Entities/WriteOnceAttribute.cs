// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Makes a property writable on insert only: the insert writes the payload's value, no update
    ///     ever sets it, and a save that changes it on an existing row is a validation error at the
    ///     property's path.
    /// </summary>
    /// <remarks>A property carries at most one ownership attribute, and none on a parent key or a database-computed column.</remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class WriteOnceAttribute : Attribute;
}
