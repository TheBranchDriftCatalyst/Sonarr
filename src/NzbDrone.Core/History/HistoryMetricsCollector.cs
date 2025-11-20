using System;
using NLog;
using Prometheus;
using NzbDrone.Core.Download;
using NzbDrone.Core.Instrumentation.Metrics;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.History
{
    /// <summary>
    /// Collects Prometheus metrics for the History domain.
    /// Automatically discovered and registered by DryIoc.
    /// Tracks history events through domain events.
    /// </summary>
    public class HistoryMetricsCollector : MetricsCollectorBase,
        IHandle<EpisodeGrabbedEvent>,
        IHandle<EpisodeImportedEvent>,
        IHandle<DownloadFailedEvent>,
        IHandle<EpisodeFileDeletedEvent>,
        IHandle<EpisodeFileRenamedEvent>,
        IHandle<DownloadIgnoredEvent>
    {

        // Counters (cumulative)
        private static readonly Counter HistoryEventsCounter = Metrics.CreateCounter(
            "sonarr_history_events_total",
            "History events by type",
            new CounterConfiguration { LabelNames = new[] { "event_type" } }
        );

        private static readonly Counter DownloadFailuresCounter = Metrics.CreateCounter(
            "sonarr_download_failures_total",
            "Download failures by reason",
            new CounterConfiguration { LabelNames = new[] { "reason" } }
        );

        private static readonly Counter ImportsCounter = Metrics.CreateCounter(
            "sonarr_imports_total",
            "Import events by source",
            new CounterConfiguration { LabelNames = new[] { "source" } }
        );

        // Gauges (current state)
        private static readonly Gauge LastImportTimestampGauge = Metrics.CreateGauge(
            "sonarr_last_import_timestamp_seconds",
            "Last successful import timestamp"
        );

        private static readonly Gauge LastDownloadTimestampGauge = Metrics.CreateGauge(
            "sonarr_last_download_timestamp_seconds",
            "Last download timestamp"
        );

        public HistoryMetricsCollector(Logger logger)
            : base(logger)
        {
        }

        public void Handle(EpisodeGrabbedEvent message)
        {
            HandleEvent(message, nameof(EpisodeGrabbedEvent), _ =>
            {
                HistoryEventsCounter.WithLabels("grabbed").Inc();
                LastDownloadTimestampGauge.SetToCurrentTimeUtc();

                _logger.Trace("History event: grabbed");
            });
        }

        public void Handle(EpisodeImportedEvent message)
        {
            HandleEvent(message, nameof(EpisodeImportedEvent), msg =>
            {
                var source = msg.NewDownload ? "download" : "manual";

                HistoryEventsCounter.WithLabels("downloaded").Inc();
                ImportsCounter.WithLabels(source).Inc();
                LastImportTimestampGauge.SetToCurrentTimeUtc();

                _logger.Trace("History event: downloaded, source={0}", source);
            });
        }

        public void Handle(DownloadFailedEvent message)
        {
            HandleEvent(message, nameof(DownloadFailedEvent), msg =>
            {
                HistoryEventsCounter.WithLabels("failed").Inc();

                var reason = MetricLabelSanitizer.SanitizeFailureReason(msg.Message);
                DownloadFailuresCounter.WithLabels(reason).Inc();

                _logger.Trace("History event: failed, reason={Reason}", reason);
            });
        }

        public void Handle(EpisodeFileDeletedEvent message)
        {
            HandleEvent(message, nameof(EpisodeFileDeletedEvent), _ =>
            {
                HistoryEventsCounter.WithLabels("deleted").Inc();

                _logger.Trace("History event: deleted");
            });
        }

        public void Handle(EpisodeFileRenamedEvent message)
        {
            HandleEvent(message, nameof(EpisodeFileRenamedEvent), _ =>
            {
                HistoryEventsCounter.WithLabels("renamed").Inc();

                _logger.Trace("History event: renamed");
            });
        }

        public void Handle(DownloadIgnoredEvent message)
        {
            HandleEvent(message, nameof(DownloadIgnoredEvent), _ =>
            {
                HistoryEventsCounter.WithLabels("ignored").Inc();

                _logger.Trace("History event: ignored");
            });
        }

        public override void UpdateGauges()
        {
            // No periodic updates needed - all metrics are event-driven
        }
    }
}
