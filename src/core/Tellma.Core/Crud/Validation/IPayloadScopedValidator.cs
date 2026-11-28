// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Crud.Validation
{
    // Spec 0015 §5.1, under its interim rule: a re-entry composed for a moved permission set fails
    // closed when an enlisted group was validated under the old set. A validator that can tell it
    // judged nothing over a payload lets such a group replay instead, as 0018 §3.3 expects of the
    // invite's welcome emails.
    /// <summary>
    ///     A validator that judges nothing over some payloads. An enlisted group whose service hooks no
    ///     validation and whose validators are all of this kind, judging nothing over its rows,
    ///     counts as unjudged, so a re-entry composed for another permission set replays it as it is.
    /// </summary>
    /// <remarks>
    ///     Only a validator whose verdict on the payloads it does judge reads nothing a permission
    ///     set decides may implement this; one that reads the caller's grants must not, whatever
    ///     the payload.
    /// </remarks>
    /// <typeparam name="TEntity">The stack's leaf.</typeparam>
    internal interface IPayloadScopedValidator<in TEntity>
        where TEntity : class
    {
        /// <summary>Whether the validator judges anything over a payload.</summary>
        /// <param name="entities">The payload's roots, with the child collections they carry.</param>
        /// <returns>False when it judges nothing over them.</returns>
        bool Judges(IReadOnlyList<TEntity> entities);
    }
}
