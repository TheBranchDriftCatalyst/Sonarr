using System;
using NLog;
using Prometheus;
using NzbDrone.Core.Instrumentation.Metrics;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    /// <summary>
    /// Collects Prometheus metrics for Download events.
    /// Automatically discovered and registered by DryIoc.
    /// </summary>
    public class DownloadMetricsCollector : MetricsCollectorBase,
        IHandle<EpisodeGrabbedEvent>,
        IHandle<DownloadCompletedEvent>,
        IHandle<DownloadFailedEvent>,
        IHandle<DownloadIgnoredEvent>
    {

        // Counters (cumulative)
        private static readonly Counter DownloadsGrabbedCounter = Metrics.CreateCounter(
            "sonarr_downloads_grabbed_total",
            "Downloads grabbed",
            new CounterConfiguration { LabelNames = new[] { "protocol", "indexer" } }
        );

        private static readonly Counter DownloadsCompletedCounter = Metrics.CreateCounter(
            "sonarr_downloads_completed_total",
            "Downloads completed successfully",
            new CounterConfiguration { LabelNames = new[] { "client" } }
        );

        private static readonly Counter DownloadsFailedCounter = Metrics.CreateCounter(
            "sonarr_downloads_failed_total",
            "Downloads failed",
            new CounterConfiguration { LabelNames = new[] { "client", "reason" } }
        );

        private static readonly Counter DownloadsIgnoredCounter = Metrics.CreateCounter(
            "sonarr_downloads_ignored_total",
            "Downloads ignored",
            new CounterConfiguration { LabelNames = new[] { "reason" } }
        );

        public DownloadMetricsCollector(Logger logger)
            : base(logger)
        {
        }

        public void Handle(EpisodeGrabbedEvent message)
        {
            HandleEvent(message, nameof(EpisodeGrabbedEvent), msg =>
            {
                var protocol = msg.Episode?.Release?.DownloadProtocol.ToString() ?? "unknown";
                var indexer = msg.Episode?.Release?.Indexer ?? "unknown";

                DownloadsGrabbedCounter.WithLabels(protocol, indexer).Inc();

                _logger.Trace("Download grabbed: protocol={0}, indexer={1}", protocol, indexer);
            });
        }

        public void Handle(DownloadCompletedEvent message)
        {
            HandleEvent(message, nameof(DownloadCompletedEvent), msg =>
            {
                var client = msg.TrackedDownload?.DownloadItem?.DownloadClientInfo?.Name ?? "unknown";

                DownloadsCompletedCounter.WithLabels(client).Inc();

                _logger.Trace("Download completed: client={0}", client);
            });
        }

        public void Handle(DownloadFailedEvent message)
        {
            HandleEvent(message, nameof(DownloadFailedEvent), msg =>
            {
                var client = msg.DownloadClient ?? "unknown";
                var reason = MetricLabelSanitizer.SanitizeFailureReason(msg.Message);

                DownloadsFailedCounter.WithLabels(client, reason).Inc();

                _logger.Trace("Download failed: client={Client}, reason={Reason}", client, reason);
            });
        }

        public void Handle(DownloadIgnoredEvent message)
        {
            HandleEvent(message, nameof(DownloadIgnoredEvent), msg =>
            {
                var reason = MetricLabelSanitizer.SanitizeFailureReason(msg.Message);

                DownloadsIgnoredCounter.WithLabels(reason).Inc();

                _logger.Trace("Download ignored: reason={Reason}", reason);
            });
        }

        public override void UpdateGauges()
        {
            // No gauges for download events - all counters
        }
    }
}
