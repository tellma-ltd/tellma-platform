// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The compiler looks the marker up by this exact name, so it cannot live in the folder's namespace.
#pragma warning disable IDE0130
namespace System.Runtime.CompilerServices
#pragma warning restore IDE0130
{
    /// <summary>
    ///     The marker the compiler needs for <c>init</c> accessors and records, which netstandard2.0
    ///     does not ship; internal, so it never clashes with a consumer's own.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
