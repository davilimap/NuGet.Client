// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using NuGet.Common;
using NuGet.Configuration;

namespace NuGet.PackageManagement.Telemetry
{
    public static class SourceTelemetry
    {
        [Flags]
        private enum HttpStyle
        {
            NotPresent = 0,
            YesV2 = 1,
            YesV3 = 2,
            YesV3AndV2 = YesV3 | YesV2,
        }

        private static readonly Lazy<string> ExpectedVsOfflinePackagesPath = new Lazy<string>(() =>
        {
            try
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Path.Combine(programFiles, "Microsoft SDKs", "NuGetPackages");
            }
            catch
            {
                // Ignore this check if we fail for any reason to generate the path.
                return null;
            }
        });

        /// <summary>
        /// Create a SourceSummaryEvent event with counts of local vs http and v2 vs v3 feeds.
        /// </summary>
        public static TelemetryEvent GetSourceSummaryEvent(Guid parentId, IEnumerable<PackageSource> packageSources)
        {
            var local = 0;
            var httpV2 = 0;
            var httpV3 = 0;
            var hasNuGetOrgV3 = false;
            var nugetOrg = HttpStyle.NotPresent;

            if (packageSources != null)
            {
                foreach (var source in packageSources)
                {
                    // Ignore disabled sources
                    if (source.IsEnabled)
                    {
                        if (source.IsHttp)
                        {
                            if (IsHttpV3(source))
                            {
                                // Http V3 feed
                                httpV3++;

                                if (IsHttpNuGetOrgSubdomain(source))
                                {
                                    hasNuGetOrgV3 = true;
                                    nugetOrg = HttpStyle.YesV3;
                                }
                            }
                            else
                            {
                                // Http V2 feed
                                httpV2++;

                                // Prefer v3 over v2 if v3 is found
                                if (!hasNuGetOrgV3 && IsHttpNuGetOrgSubdomain(source))
                                {
                                    nugetOrg = HttpStyle.YesV2;
                                }
                            }
                        }
                        else
                        {
                            // Local or UNC feed
                            local++;
                        }
                    }
                }
            }

            return new RestorePackageSourceSummaryTelemetryEvent(
                parentId,
                local,
                httpV2,
                httpV3,
                nugetOrg.ToString());
        }

        /// <summary>
        /// Create a SourceSummaryEvent event with counts of local vs http and v2 vs v3 feeds.
        /// </summary>
        public static TelemetryEvent GetSearchSourceSummaryEvent(Guid parentId, IEnumerable<PackageSource> packageSources)
        {
            var local = 0;
            var httpV2 = 0;
            var httpV3 = 0;
            var nugetOrg = HttpStyle.NotPresent;
            var vsOfflinePackages = false;
            var dotnetCuratedFeed = false;

            if (packageSources != null)
            {
                foreach (var source in packageSources)
                {
                    // Ignore disabled sources
                    if (source.IsEnabled)
                    {
                        if (source.IsHttp)
                        {
                            if (IsHttpV3(source))
                            {
                                // Http V3 feed
                                httpV3++;

                                if (IsHttpNuGetOrgDomainOrSubdomain(source))
                                {
                                    nugetOrg |= HttpStyle.YesV3;
                                }
                            }
                            else
                            {
                                // Http V2 feed
                                httpV2++;

                                if (IsHttpNuGetOrgDomainOrSubdomain(source))
                                {
                                    if (source.Source.IndexOf(
                                        "api/v2/curated-feeds/microsoftdotnet",
                                        StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        dotnetCuratedFeed = true;
                                    }
                                    else
                                    {
                                        nugetOrg |= HttpStyle.YesV2;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Local or UNC feed
                            local++;

                            if (StringComparer.OrdinalIgnoreCase.Equals(
                                ExpectedVsOfflinePackagesPath.Value,
                                source.Source?.TrimEnd('\\')))
                            {
                                vsOfflinePackages = true;
                            }
                        }
                    }
                }
            }

            return new SearchPackageSourceSummaryTelemetryEvent(
                parentId,
                local,
                httpV2,
                httpV3,
                nugetOrg.ToString(),
                vsOfflinePackages,
                dotnetCuratedFeed);
        }

        /// <summary>
        /// True if the source is http and ends with index.json
        /// </summary>
        private static bool IsHttpV3(PackageSource source)
        {
            return source.IsHttp &&
                (source.Source.EndsWith("index.json", StringComparison.OrdinalIgnoreCase)
                || source.ProtocolVersion == 3);
        }

        /// <summary>
        /// True if the source is http and has a *.nuget.org host.
        /// </summary>
        private static bool IsHttpNuGetOrgSubdomain(PackageSource source)
        {
            return (source.IsHttp && source.TrySourceAsUri?.Host.EndsWith(".nuget.org", StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>
        /// True if the source is HTTP and has a *.nuget.org or nuget.org host.
        /// </summary>
        private static bool IsHttpNuGetOrgDomainOrSubdomain(PackageSource source)
        {
            if (!source.IsHttp)
            {
                return false;
            }

            var uri = source.TrySourceAsUri;
            if (uri == null)
            {
                return false;
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(uri.Host, "nuget.org")
                || uri.Host.EndsWith(".nuget.org", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        // NumLocalFeeds(c:\ or \\ or file:///)
        // NumHTTPv2Feeds
        // NumHTTPv3Feeds
        // NuGetOrg: [NotPresent | YesV2 | YesV3]
        private class RestorePackageSourceSummaryTelemetryEvent : TelemetryEvent
        {
            public RestorePackageSourceSummaryTelemetryEvent(
                Guid parentId,
                int local,
                int httpV2,
                int httpV3,
                string nugetOrg) : base("RestorePackageSourceSummary")
            {
                this["NumLocalFeeds"] = local;
                this["NumHTTPv2Feeds"] = httpV2;
                this["NumHTTPv3Feeds"] = httpV3;
                this["NuGetOrg"] = nugetOrg;
                this["ParentId"] = parentId.ToString();
            }
        }

        // NumLocalFeeds(c:\ or \\ or file:///)
        // NumHTTPv2Feeds
        // NumHTTPv3Feeds
        // NuGetOrg: [NotPresent | YesV2 | YesV3]
        // VsOfflinePackages: [true | false]
        // DotnetCuratedFeed: [true | false]
        private class SearchPackageSourceSummaryTelemetryEvent : TelemetryEvent
        {
            public SearchPackageSourceSummaryTelemetryEvent(
                Guid parentId,
                int local,
                int httpV2,
                int httpV3,
                string nugetOrg,
                bool vsOfflinePackages,
                bool dotnetCuratedFeed)
                : base("SearchPackageSourceSummary")
            {
                this["NumLocalFeeds"] = local;
                this["NumHTTPv2Feeds"] = httpV2;
                this["NumHTTPv3Feeds"] = httpV3;
                this["NuGetOrg"] = nugetOrg;
                this["VsOfflinePackages"] = vsOfflinePackages;
                this["DotnetCuratedFeed"] = dotnetCuratedFeed;
                this["ParentId"] = parentId.ToString();
            }
        }
    }
}
