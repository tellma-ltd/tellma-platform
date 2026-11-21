// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Serilog;
using System.Collections;
using System.Globalization;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.AspNetCore.Tenancy
{
    /// <summary>
    ///     The logger scope of a tenant request: <c>TenantId</c>, <c>TenantCategory</c>, <c>Subject</c>
    ///     and <c>UserId</c>. The members are read from the request's context each time a log event
    ///     reads the scope, so a line written after the access guard names the user it bound.
    /// </summary>
    /// <param name="accessor">The request's context accessor.</param>
    internal sealed class TenantLogScope(IRequestContextAccessor accessor) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        /// <summary>The scope's member names, in order.</summary>
        private static readonly string[] Names = ["TenantId", "TenantCategory", "Subject", "UserId"];

        /// <inheritdoc />
        public int Count => Names.Length;

        /// <summary>
        ///     Puts a tenant request's members on the request-completion event, which is written after
        ///     the scope has closed. A tenantless request carries none.
        /// </summary>
        /// <param name="diagnostics">The request's diagnostic context.</param>
        /// <param name="accessor">The request's context accessor, if the request has one.</param>
        public static void Enrich(IDiagnosticContext diagnostics, IRequestContextAccessor? accessor)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);
            if (accessor?.Current.Tenant is null)
            {
                return;
            }

            foreach (KeyValuePair<string, object?> member in new TenantLogScope(accessor))
            {
                diagnostics.Set(member.Key, member.Value);
            }
        }

        /// <inheritdoc />
        public KeyValuePair<string, object?> this[int index]
        {
            get
            {
                RequestContext context = accessor.Current;
                object? value = index switch
                {
                    0 => context.Tenant?.Id,
                    1 => context.Tenant?.Category.ToString(),
                    2 => context.Subject,
                    3 => context.UserId,
                    _ => throw new ArgumentOutOfRangeException(nameof(index), index, "The scope has four members."),
                };
                return new KeyValuePair<string, object?>(Names[index], value);
            }
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (int i = 0; i < Names.Length; i++)
            {
                yield return this[i];
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            // What a text sink prints for the scope; the subject identifies a person, so it is left out.
            RequestContext context = accessor.Current;
            return string.Create(CultureInfo.InvariantCulture, $"TenantId:{context.Tenant?.Id} UserId:{context.UserId}");
        }
    }
}
