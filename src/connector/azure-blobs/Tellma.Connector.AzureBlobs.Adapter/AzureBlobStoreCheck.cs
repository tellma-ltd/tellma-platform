// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Connector.AzureBlobs
{
    // Spec 0017 §9: the Azure store's settings fail the host before it serves traffic. The runtime's
    // own blob checks cannot see this adapter's settings (the runtime never references an adapter),
    // so the adapter reports them through a check of its own.

    /// <summary>
    ///     The startup check of the Azure blob store: its settings are valid (exactly one way to reach
    ///     the account, a container prefix every tenant id can extend, positive limits) and describe a
    ///     client that can be built, a connection string that parses or an endpoint with a credential.
    ///     It sends nothing to the account.
    /// </summary>
    internal sealed class AzureBlobStoreCheck : IStartupCheck
    {
        /// <summary>The check's name.</summary>
        public const string CheckName = "blobs.azure";

        /// <inheritdoc />
        public string Name => CheckName;

        /// <summary>Deployment settings, so a host composed only to be inspected does not run it.</summary>
        public bool RequiresInfrastructure => true;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];
            string fix = $"Set '{AzureBlobStoreOptions.SectionName}:{nameof(AzureBlobStoreOptions.ServiceUri)}' to the storage account's blob endpoint, "
                + $"or, for Azurite and local development only, '{AzureBlobStoreOptions.SectionName}:{nameof(AzureBlobStoreOptions.ConnectionString)}'; "
                + "correct any other setting the problem names.";

            // --- The settings: resolving them runs the validator ---
            AzureBlobStoreOptions options;
            try
            {
                options = services.GetRequiredService<IOptions<AzureBlobStoreOptions>>().Value;
            }
            catch (OptionsValidationException exception)
            {
                problems.AddRange(exception.Failures.Select(failure => new CompositionProblem(Name, failure, fix)));
                return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
            }
            catch (InvalidOperationException exception)
            {
                problems.Add(new CompositionProblem(Name, $"The Azure blob store's settings cannot be bound: {exception.Message}", fix));
                return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
            }

            // --- The client they describe, built the way the store builds it ---
            try
            {
                _ = AzureBlobStore.CreateClient(options, services.GetRequiredService<AzureBlobStoreCredential>().Credential);
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException)
            {
                problems.Add(new CompositionProblem(Name, $"The Azure blob store's client cannot be built: {exception.Message}", fix));
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
