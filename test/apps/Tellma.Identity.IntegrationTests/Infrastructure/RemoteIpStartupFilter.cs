// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Gives every test request a client address. <c>TestServer</c> leaves
    ///     <c>Connection.RemoteIpAddress</c> null, so per-IP rate limiting and the IP stamped on
    ///     audit rows would be silently untestable — an assertion on them would pass vacuously or
    ///     fail for the harness rather than the product.
    /// </summary>
    /// <param name="address">The address to present as the caller's.</param>
    public sealed class RemoteIpStartupFilter(string address) : IStartupFilter
    {
        /// <inheritdoc />
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            var parsed = IPAddress.Parse(address);
            return app =>
            {
                app.Use(async (context, following) =>
                {
                    context.Connection.RemoteIpAddress = parsed;
                    await following(context);
                });

                next(app);
            };
        }
    }
}
