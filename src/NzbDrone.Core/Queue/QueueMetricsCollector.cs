using System;
using System.Linq;
using NLog;
using Prometheus;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Instrumentation.Metrics;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Queue
{
    /// <summary>
    /// Collects Prometheus metrics for the Download Queue domain.
    /// Automatically discovered and registered by DryIoc.
    /// </summary>
    public class QueueMetricsCollector : MetricsCollectorBase,
        IHandle<QueueUpdatedEvent>
    {
        private readonly IQueueService _queueService;

        // Gauges (current state)
        private static readonly Gauge QueueTotalGauge = Metrics.CreateGauge(
            "sonarr_queue_total",
            "Current queue size",
            new GaugeConfiguration { LabelNames = new[] { "status" } }
        );

        private static readonly Gauge QueueByProtocolGauge = Metrics.CreateGauge(
            "sonarr_queue_by_protocol",
            "Queue items by protocol",
            new GaugeConfiguration { LabelNames = new[] { "protocol" } }
        );

        private static readonly Gauge QueueByClientGauge = Metrics.CreateGauge(
            "sonarr_queue_by_client",
            "Queue items by download client",
            new GaugeConfiguration { LabelNames = new[] { "client_name" } }
        );

        private static readonly Gauge QueueSizeRemainingBytesGauge = Metrics.CreateGauge(
            "sonarr_queue_size_remaining_bytes",
            "Queue size remaining (bytes)"
        );

        private static readonly Gauge QueueSizeTotalBytesGauge = Metrics.CreateGauge(
            "sonarr_queue_size_total_bytes",
            "Total queue size (bytes)"
        );

        private static readonly Gauge QueueTimeRemainingSecondsGauge = Metrics.CreateGauge(
            "sonarr_queue_time_remaining_seconds",
            "Estimated time remaining (seconds)"
        );

        public QueueMetricsCollector(IQueueService queueService, Logger logger)
            : base(logger)
        {
            _queueService = queueService;

            try
            {
                UpdateGauges();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize queue metrics on startup");
            }
        }

        public void Handle(QueueUpdatedEvent message)
        {
            HandleEvent(message, nameof(QueueUpdatedEvent), _ => UpdateGauges());
        }

        public override void UpdateGauges()
        {
            try
            {
                var queue = _queueService.GetQueue();

                // Queue by status - reset all enum values to 0 first to clean up old labels
                var statusCounts = queue.GroupBy(q => q.Status.ToString().ToLower())
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (QueueStatus status in Enum.GetValues(typeof(QueueStatus)))
                {
                    var statusKey = status.ToString().ToLower();
                    var count = statusCounts.TryGetValue(statusKey, out var sc) ? sc : 0;
                    QueueTotalGauge.WithLabels(statusKey).Set(count);
                }

                // Queue by protocol - reset all enum values to 0
                var protocolCounts = queue.GroupBy(q => q.Protocol.ToString().ToLower())
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (DownloadProtocol protocol in Enum.GetValues(typeof(DownloadProtocol)))
                {
                    var protocolKey = protocol.ToString().ToLower();
                    var count = protocolCounts.TryGetValue(protocolKey, out var pc) ? pc : 0;
                    QueueByProtocolGauge.WithLabels(protocolKey).Set(count);
                }

                // Queue by download client - limit cardinality by grouping unknown clients
                var byClient = queue.GroupBy(q => MetricLabelSanitizer.SanitizeClientName(q.DownloadClient));
                foreach (var group in byClient)
                {
                    QueueByClientGauge.WithLabels(group.Key).Set(group.Count());
                }

                // Total size metrics
                var totalSize = queue.Sum(q => (double)q.Size);
                var remainingSize = queue.Sum(q => (double)q.SizeLeft);
                QueueSizeTotalBytesGauge.Set(totalSize);
                QueueSizeRemainingBytesGauge.Set(remainingSize);

                // Time remaining (average for all downloads with time estimates)
                var itemsWithTime = queue.Where(q => q.TimeLeft.HasValue).ToList();
                if (itemsWithTime.Any())
                {
                    var avgTimeRemaining = itemsWithTime.Average(q => q.TimeLeft.Value.TotalSeconds);
                    QueueTimeRemainingSecondsGauge.Set(avgTimeRemaining);
                }
                else
                {
                    QueueTimeRemainingSecondsGauge.Set(0);
                }

                _logger.Trace("Updated queue metrics: {QueueCount} items, {TotalSize} bytes, {RemainingSize} bytes",
                    queue.Count, totalSize, remainingSize);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update queue gauge metrics");
            }
        }
    }
}
