// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tellma.Identity.Services.Seeding
{
    /// <summary>
    ///     Runs <see cref="IdentitySeeder" /> once at startup, before the host begins serving
    ///     traffic (and before the Quartz scheduler touches the store).
    /// </summary>
    /// <param name="serviceProvider">The root provider used to create the seeding scope.</param>
    public sealed class IdentitySeedHostedService(IServiceProvider serviceProvider) : IHostedService
    {
        /// <inheritdoc />
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
            IdentitySeeder seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
            await seeder.SeedAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
