// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Connector.AcsEmail.Adapter.Tests.Infrastructure;
using Tellma.Core.Testing.Email;

namespace Tellma.Connector.AcsEmail.Adapter.Tests.Conformance
{
    /// <summary>
    ///     The ACS transport answering the shared contract. The no-retry case doubles as the
    ///     behavioural proof that the Azure pipeline's retry policy really was zeroed: an
    ///     unconfigured pipeline would answer a scripted refusal with three more requests.
    /// </summary>
    public class AcsEmailConformanceTests : EmailSenderConformanceTests
    {
        /// <inheritdoc />
        protected override ValueTask<IEmailSenderHarness> CreateHarnessAsync()
        {
            return ValueTask.FromResult<IEmailSenderHarness>(new AcsSenderHarness());
        }
    }
}
