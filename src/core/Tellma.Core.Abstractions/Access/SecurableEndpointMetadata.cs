// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     Endpoint metadata naming the securable a tenant endpoint requires. Stamped on every
    ///     projected stack endpoint and, through the host's <c>RequireSecurable(resource, action)</c>
    ///     convention, on a hand-mapped one.
    /// </summary>
    /// <remarks>
    ///     Metadata for the startup audit, OpenAPI and step-up, never enforcement: the securable is
    ///     evaluated by the service pipeline and the action invoker, where every caller passes. Every
    ///     tenant endpoint carries exactly one of this record and <see cref="MemberEndpointMetadata" />,
    ///     and the pair named here must be registered; the startup audit fails on either violation.
    /// </remarks>
    /// <param name="Resource">The securable's resource, such as <c>gl.Center</c>.</param>
    /// <param name="Action">The securable's action, such as <c>Save</c>.</param>
    public sealed record SecurableEndpointMetadata(string Resource, string Action)
    {
        /// <inheritdoc cref="SecurableEndpointMetadata(string, string)" />
        public string Resource { get; } = Validate(Resource, nameof(Resource));

        /// <inheritdoc cref="SecurableEndpointMetadata(string, string)" />
        public string Action { get; } = Validate(Action, nameof(Action));

        /// <summary>Refuses an empty name; the grammar is the startup audit's to check.</summary>
        /// <param name="value">The name.</param>
        /// <param name="parameterName">The parameter it came from.</param>
        /// <returns>The name.</returns>
        private static string Validate(string value, string parameterName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
            return value;
        }
    }
}
