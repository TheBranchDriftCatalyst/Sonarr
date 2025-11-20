using System;
using System.Linq;
using NLog;
using Prometheus;
using NzbDrone.Core.Instrumentation.Metrics;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Indexers
{
    /// <summary>
    /// Collects Prometheus metrics for the Indexer domain.
    /// Automatically discovered and registered by DryIoc.
    /// </summary>
    public class IndexerMetricsCollector : MetricsCollectorBase,
        IHandle<RssSyncCompleteEvent>
    {
        private readonly IIndexerFactory _indexerFactory;

        // Gauges (current state)
        private static readonly Gauge IndexersEnabledGauge = Metrics.CreateGauge(
            "sonarr_indexers_enabled",
            "Indexers enabled vs disabled",
            new GaugeConfiguration { LabelNames = new[] { "enabled" } }
        );

        private static readonly Gauge IndexersByProtocolGauge = Metrics.CreateGauge(
            "sonarr_indexers_by_protocol",
            "Indexers by protocol",
            new GaugeConfiguration { LabelNames = new[] { "protocol" } }
        );

        private static readonly Gauge IndexersRssEnabledGauge = Metrics.CreateGauge(
            "sonarr_indexers_rss_enabled",
            "Indexers with RSS enabled"
        );

        private static readonly Gauge IndexersSearchEnabledGauge = Metrics.CreateGauge(
            "sonarr_indexers_search_enabled",
            "Indexers with search enabled",
            new GaugeConfiguration { LabelNames = new[] { "search_type" } }
        );

        // Counters (cumulative)
        private static readonly Counter RssSyncCounter = Metrics.CreateCounter(
            "sonarr_indexer_rss_syncs_total",
            "RSS sync operations completed"
        );

        private static readonly Counter RssSyncDecisionsCounter = Metrics.CreateCounter(
            "sonarr_indexer_rss_decisions_total",
            "RSS sync decisions",
            new CounterConfiguration { LabelNames = new[] { "decision_type" } }
        );

        public IndexerMetricsCollector(IIndexerFactory indexerFactory, Logger logger)
            : base(logger)
        {
            _indexerFactory = indexerFactory;

            try
            {
                UpdateGauges();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize indexer metrics on startup");
            }
        }

        public void Handle(RssSyncCompleteEvent message)
        {
            HandleEvent(message, nameof(RssSyncCompleteEvent), msg =>
            {
                RssSyncCounter.Inc();

                // Track decisions
                if (msg.ProcessedDecisions != null)
                {
                    var approved = msg.ProcessedDecisions.Grabbed?.Count ?? 0;
                    var rejected = msg.ProcessedDecisions.Rejected?.Count ?? 0;

                    if (approved > 0)
                    {
                        RssSyncDecisionsCounter.WithLabels("approved").Inc(approved);
                    }

                    if (rejected > 0)
                    {
                        RssSyncDecisionsCounter.WithLabels("rejected").Inc(rejected);
                    }
                }

                _logger.Trace("RSS sync completed");
            });
        }

        public override void UpdateGauges()
        {
            try
            {
                var allIndexers = _indexerFactory.All();

                // Enabled vs disabled
                var enabled = allIndexers.Count(i => i.Enable);
                var disabled = allIndexers.Count - enabled;
                IndexersEnabledGauge.WithLabels("true").Set(enabled);
                IndexersEnabledGauge.WithLabels("false").Set(disabled);

                // By protocol
                var byProtocol = allIndexers.GroupBy(i => i.Protocol);
                foreach (var group in byProtocol)
                {
                    IndexersByProtocolGauge.WithLabels(group.Key.ToString()).Set(group.Count());
                }

                // RSS enabled
                var rssEnabled = allIndexers.Count(i => i.EnableRss);
                IndexersRssEnabledGauge.Set(rssEnabled);

                // Search enabled
                var automaticSearchEnabled = allIndexers.Count(i => i.EnableAutomaticSearch);
                var interactiveSearchEnabled = allIndexers.Count(i => i.EnableInteractiveSearch);
                IndexersSearchEnabledGauge.WithLabels("automatic").Set(automaticSearchEnabled);
                IndexersSearchEnabledGauge.WithLabels("interactive").Set(interactiveSearchEnabled);

                _logger.Trace("Updated indexer metrics: {0} total, {1} enabled, {2} RSS enabled",
                    allIndexers.Count, enabled, rssEnabled);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update indexer gauge metrics");
            }
        }
    }
}
