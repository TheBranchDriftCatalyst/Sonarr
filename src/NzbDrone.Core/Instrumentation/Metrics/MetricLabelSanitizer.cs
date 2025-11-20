using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Instrumentation.Metrics
{
    /// <summary>
    /// Utility class for sanitizing metric labels to prevent cardinality explosion.
    /// Shared across all metric collectors to maintain DRY principles.
    /// </summary>
    public static class MetricLabelSanitizer
    {
        // Regex patterns for sanitizing error messages (compiled for performance)
        private static readonly Regex IpAddressRegex = new Regex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled);
        private static readonly Regex NumbersRegex = new Regex(@"\d+", RegexOptions.Compiled);
        private static readonly Regex PathRegex = new Regex(@"[/\\][\w/\\.-]*", RegexOptions.Compiled);

        // Known download clients to limit label cardinality
        private static readonly HashSet<string> KnownDownloadClients = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SABnzbd", "NZBGet", "NZBVortex", "UsenetBlackhole",
            "qBittorrent", "Transmission", "Deluge", "RTorrent", "uTorrent",
            "Vuze", "DownloadStation", "TorrentBlackhole", "Aria2", "Hadouken",
            "Flood", "Pneumatic"
        };

        // Known commands to limit label cardinality
        private static readonly HashSet<string> KnownCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RefreshSeries", "RssSyncCommand", "EpisodeSearchCommand", "SeasonSearchCommand",
            "SeriesSearchCommand", "ApplicationUpdateCheckCommand", "BackupCommand",
            "HousekeepingCommand", "MessagingCleanupCommand", "RefreshMonitoredDownloadsCommand",
            "UpdateMetricsCommand", "CheckHealthCommand", "UpdateSceneMappingCommand",
            "CleanUpRecycleBinCommand", "ImportListSyncCommand"
        };

        /// <summary>
        /// Sanitizes error/failure messages to prevent high cardinality in metric labels.
        /// Buckets common error patterns and removes dynamic content (IPs, paths, numbers).
        /// </summary>
        public static string SanitizeFailureReason(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "unknown";
            }

            var reason = message.ToLower().Trim();

            // Bucket common error patterns to prevent cardinality explosion
            if (reason.Contains("connection") || reason.Contains("connect"))
                return "connection_failed";
            if (reason.Contains("timeout") || reason.Contains("timed out"))
                return "timeout";
            if (reason.Contains("permission") || reason.Contains("forbidden") ||
                reason.Contains("unauthorized") || reason.Contains("401") || reason.Contains("403"))
                return "permission_denied";
            if (reason.Contains("not found") || reason.Contains("404"))
                return "not_found";
            if (reason.Contains("certificate") || reason.Contains("ssl") || reason.Contains("tls"))
                return "certificate_error";
            if (reason.Contains("disk") || reason.Contains("space") || reason.Contains("storage"))
                return "disk_error";
            if (reason.Contains("network") || reason.Contains("dns"))
                return "network_error";

            // Remove dynamic content (IPs, numbers, paths)
            reason = IpAddressRegex.Replace(reason, "IP");
            reason = PathRegex.Replace(reason, "");
            reason = NumbersRegex.Replace(reason, "N");

            // Truncate to prevent very long error messages
            if (reason.Length > 30)
            {
                return "error";
            }

            return reason;
        }

        /// <summary>
        /// Sanitizes download client names to limit label cardinality.
        /// Groups unknown/custom clients together.
        /// </summary>
        public static string SanitizeClientName(string clientName)
        {
            if (string.IsNullOrWhiteSpace(clientName))
            {
                return "unknown";
            }

            // Limit length to prevent very long names
            var sanitized = clientName.Trim();
            if (sanitized.Length > 50)
            {
                sanitized = sanitized.Substring(0, 50);
            }

            // Check if it's a known client (case-insensitive)
            foreach (var knownClient in KnownDownloadClients)
            {
                if (sanitized.IndexOf(knownClient, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return knownClient.ToLower();
                }
            }

            // Unknown/custom clients grouped together to limit cardinality
            return "custom";
        }

        /// <summary>
        /// Sanitizes command names to limit label cardinality.
        /// Groups unknown/custom commands together.
        /// </summary>
        public static string SanitizeCommandName(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return "unknown";
            }

            var sanitized = commandName.Trim();

            // Check if it's a known command (case-insensitive)
            foreach (var knownCommand in KnownCommands)
            {
                if (sanitized.Equals(knownCommand, StringComparison.OrdinalIgnoreCase))
                {
                    return knownCommand;
                }
            }

            // Unknown/custom commands grouped together to limit cardinality
            return "other";
        }

        /// <summary>
        /// Sanitizes provider names (notifications, import lists, indexers) to limit label cardinality.
        /// Limits length and removes special characters.
        /// </summary>
        public static string SanitizeProviderName(string providerName, int maxLength = 50)
        {
            if (string.IsNullOrWhiteSpace(providerName))
            {
                return "unknown";
            }

            var sanitized = providerName.Trim();

            // Remove or replace problematic characters
            sanitized = Regex.Replace(sanitized, @"[^\w\s\-_]", "_");

            // Limit length
            if (sanitized.Length > maxLength)
            {
                sanitized = sanitized.Substring(0, maxLength);
            }

            return sanitized.ToLower();
        }
    }
}
