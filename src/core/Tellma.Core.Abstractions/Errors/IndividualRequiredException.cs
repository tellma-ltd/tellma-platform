// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     The operation requires an individual, and the caller is a service account: a sensitive
    ///     securable, or an endpoint that demands an assurance level, which a service principal has no
    ///     interactive sign-in to step up to. Maps to 403 with the problem code
    ///     <c>individual-required</c>.
    /// </summary>
    public sealed class IndividualRequiredException()
        : TellmaException(ProblemCode, "The operation requires an individual user; the caller is a service account.")
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "individual-required";
    }
}
