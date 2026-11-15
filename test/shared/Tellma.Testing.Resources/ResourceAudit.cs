// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Testing.Resources
{
    /// <summary>What one audit checks: the neutral resource files, and what the model and the source use.</summary>
    public sealed class ResourceAuditRequest
    {
        /// <summary>
        ///     The neutral resource files audited, such as a feature's <c>Resources/Strings.resx</c>
        ///     and <c>Resources/ServerStrings.resx</c>; each one's satellites beside it are audited
        ///     against it.
        /// </summary>
        public required IReadOnlyList<string> NeutralFiles { get; init; }

        /// <summary>The keys the composed model derives by convention.</summary>
        public required IReadOnlySet<string> DerivedKeys { get; init; }

        /// <summary>The string literals of the source that may look keys up.</summary>
        public required IReadOnlySet<string> Literals { get; init; }

        /// <summary>The rules that turn a literal into the keys it uses; the platform's conventions by default.</summary>
        public IReadOnlyList<LiteralKeyRule> Rules { get; init; } = LiteralKeyRules.Platform;

        /// <summary>
        ///     Keys a neutral file declares ahead of the code that will use them, each with the
        ///     reason it waits. A pending key is not an orphan; a pending key that is used, or that no
        ///     audited file declares, is reported so the list never outlives its reasons.
        /// </summary>
        public IReadOnlyDictionary<string, string> Pending { get; init; } = new Dictionary<string, string>();
    }

    /// <summary>The kind of an audit finding.</summary>
    public enum ResourceAuditFindingKind
    {
        /// <summary>A neutral key nothing derives and no literal spells: nothing ever looks it up.</summary>
        Orphan,

        /// <summary>A satellite key its neutral file does not declare: a translation of nothing.</summary>
        SatelliteOnly,

        /// <summary>A pending key that is now used, or that no audited file declares.</summary>
        StalePending,
    }

    /// <summary>One thing the audit found.</summary>
    /// <param name="Kind">What was found.</param>
    /// <param name="File">The resource file the key is in; empty for a pending key no file declares.</param>
    /// <param name="Key">The key.</param>
    public sealed record ResourceAuditFinding(ResourceAuditFindingKind Kind, string File, string Key)
    {
        /// <summary>The finding as one line a failing test prints.</summary>
        /// <returns>The line.</returns>
        public override string ToString()
        {
            return Kind switch
            {
                ResourceAuditFindingKind.Orphan =>
                    $"{File}: '{Key}' is neither derived by convention from the composed model nor spelled by a string literal of the source, so nothing looks it up. Remove it, or use it.",
                ResourceAuditFindingKind.SatelliteOnly =>
                    $"{File}: '{Key}' is not declared by the neutral file beside it, so it translates nothing. Remove it, or declare it in the neutral file.",
                ResourceAuditFindingKind.StalePending when File.Length == 0 =>
                    $"'{Key}' is listed as pending, but no audited file declares it. Remove it from the pending list.",
                _ =>
                    $"{File}: '{Key}' is listed as pending, but it is used now. Remove it from the pending list.",
            };
        }
    }

    /// <summary>
    ///     The resource audit: every key of each neutral shared resource file is derived by
    ///     convention from the composed model, or is spelled by a string literal of the source
    ///     (outright, or through a rule such as a dotted code or a problem code), or is pending; and
    ///     every satellite key is declared by its neutral file.
    /// </summary>
    public static class ResourceAudit
    {
        /// <summary>Runs an audit.</summary>
        /// <param name="request">What to audit.</param>
        /// <returns>The findings, by file and then key; empty for sound resources.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request" /> is null.</exception>
        public static IReadOnlyList<ResourceAuditFinding> Run(ResourceAuditRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            // --- What is used: the derived keys, then every key a literal stands for ---
            HashSet<string> used = new(request.DerivedKeys, StringComparer.Ordinal);
            foreach (string literal in request.Literals)
            {
                foreach (LiteralKeyRule rule in request.Rules)
                {
                    used.UnionWith(rule(literal));
                }
            }

            List<ResourceAuditFinding> findings = [];
            HashSet<string> declared = new(StringComparer.Ordinal);
            foreach (string neutral in request.NeutralFiles)
            {
                // --- The neutral file: each key used or pending ---
                IReadOnlyDictionary<string, string> entries = ResxFile.Read(neutral);
                declared.UnionWith(entries.Keys);
                foreach (string key in entries.Keys.Order(StringComparer.Ordinal))
                {
                    bool isUsed = used.Contains(key);
                    bool isPending = request.Pending.ContainsKey(key);
                    if (!isUsed && !isPending)
                    {
                        findings.Add(new ResourceAuditFinding(ResourceAuditFindingKind.Orphan, neutral, key));
                    }
                    else if (isUsed && isPending)
                    {
                        findings.Add(new ResourceAuditFinding(ResourceAuditFindingKind.StalePending, neutral, key));
                    }
                }

                // --- Its satellites: each key one the neutral file declares ---
                foreach ((_, string satellite) in ResxFile.Satellites(neutral))
                {
                    findings.AddRange(ResxFile.Read(satellite).Keys
                        .Where(key => !entries.ContainsKey(key))
                        .Order(StringComparer.Ordinal)
                        .Select(key => new ResourceAuditFinding(ResourceAuditFindingKind.SatelliteOnly, satellite, key)));
                }
            }

            // --- The pending list: each entry a key some audited file declares ---
            findings.AddRange(request.Pending.Keys
                .Where(key => !declared.Contains(key))
                .Order(StringComparer.Ordinal)
                .Select(static key => new ResourceAuditFinding(ResourceAuditFindingKind.StalePending, string.Empty, key)));

            return findings;
        }

        /// <summary>The findings as a report a failing test prints: one line each.</summary>
        /// <param name="findings">The findings.</param>
        /// <returns>The report; empty when there are none.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="findings" /> is null.</exception>
        public static string Report(IEnumerable<ResourceAuditFinding> findings)
        {
            ArgumentNullException.ThrowIfNull(findings);

            StringBuilder report = new();
            foreach (ResourceAuditFinding finding in findings)
            {
                _ = report.AppendLine(finding.ToString());
            }

            return report.ToString();
        }
    }
}
