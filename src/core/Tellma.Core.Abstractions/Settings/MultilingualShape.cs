// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     How many content languages a tenant has, which decides which twins of a multilingual text
    ///     group (<c>Name</c>, <c>Name2</c>, <c>Name3</c>) exist for it. It keys the query schema: at
    ///     most three schemas per process, each shared by every tenant of that shape.
    /// </summary>
    public enum MultilingualShape
    {
        /// <summary>One language: only the primary property.</summary>
        Primary = 1,

        /// <summary>Two languages: the primary property and its second twin.</summary>
        PrimaryAndSecondary = 2,

        /// <summary>Three languages: the primary property and both twins.</summary>
        All = 3,
    }
}
