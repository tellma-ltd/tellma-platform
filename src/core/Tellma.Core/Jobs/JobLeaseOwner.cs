// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §5.1 and §6.10: the lease owner every claim and the heartbeat stamp record, for
    // diagnostics only: `<instance id>/<process id>`, the instance being App Service's
    // WEBSITE_INSTANCE_ID where it is set and the machine's name elsewhere, at most the column's 128
    // characters.
    /// <summary>The instance and process the leases of this process name.</summary>
    internal static class JobLeaseOwner
    {
        /// <summary>The environment variable App Service sets to the instance's id.</summary>
        public const string InstanceIdVariable = "WEBSITE_INSTANCE_ID";

        /// <summary>This process's owner string.</summary>
        public static string Current { get; } = Of(Environment.GetEnvironmentVariable(InstanceIdVariable), Environment.MachineName, Environment.ProcessId);

        /// <summary>The owner string of an instance and a process, its instance part shortened so the whole fits the column.</summary>
        /// <param name="instanceId">The hosting platform's instance id; null or blank where it sets none.</param>
        /// <param name="machineName">The machine's name, which stands in for a missing instance id.</param>
        /// <param name="processId">The process's id.</param>
        /// <returns>The owner string, at most 128 characters.</returns>
        public static string Of(string? instanceId, string machineName, int processId)
        {
            ArgumentNullException.ThrowIfNull(machineName);

            // The process id always survives whole: it tells two processes of one instance apart.
            string instance = string.IsNullOrWhiteSpace(instanceId) ? machineName : instanceId.Trim();
            string process = string.Create(CultureInfo.InvariantCulture, $"/{processId}");
            int room = JobConfiguration.LeaseOwnerLength - process.Length;
            return (instance.Length > room ? instance[..room] : instance) + process;
        }
    }
}
