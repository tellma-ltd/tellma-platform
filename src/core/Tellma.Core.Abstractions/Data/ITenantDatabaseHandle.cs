// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     A tenant database opened outside any tenant scope by <see cref="ITenantDatabaseFactory" />,
    ///     bound to one tenant and holding one connection until it is disposed.
    /// </summary>
    /// <remarks>
    ///     A handle has no request context, so it creates <see cref="BatchPurpose.Maintenance" />
    ///     batches only: <see cref="ITenantDatabase.CreateBatch" /> with any other purpose,
    ///     <see cref="ITenantDatabase.Schema" /> and <see cref="ITenantDatabase.Context" /> throw
    ///     <see cref="InvalidOperationException" />. Its batches run one at a time on the handle's
    ///     connection.
    /// </remarks>
    public interface ITenantDatabaseHandle : ITenantDatabase, IAsyncDisposable
    {
    }
}
