using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Prometheus;
using NzbDrone.Core.Instrumentation.Metrics;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    /// <summary>
    /// Collects Prometheus metrics for the Series domain.
    /// This class is automatically discovered and registered by DryIoc.
    /// Event handlers are automatically called by EventAggregator.
    /// </summary>
    public class SeriesMetricsCollector : MetricsCollectorBase,
        IHandle<SeriesAddedEvent>,
        IHandle<SeriesDeletedEvent>,
        IHandle<SeriesUpdatedEvent>
    {
        private readonly ISeriesService _seriesService;

        // Debounce UpdateGauges to prevent rapid DB queries during batch operations
        protected override TimeSpan DebounceInterval => TimeSpan.FromSeconds(5);

        // Define metrics as static fields (created once, reused forever)
        private static readonly Counter SeriesAddedCounter = Metrics.CreateCounter(
            "sonarr_series_added_total",
            "Total number of series added to Sonarr"
        );

        private static readonly Counter SeriesDeletedCounter = Metrics.CreateCounter(
            "sonarr_series_deleted_total",
            "Total number of series deleted from Sonarr"
        );

        private static readonly Gauge SeriesTotalGauge = Metrics.CreateGauge(
            "sonarr_series_total",
            "Current number of series in library",
            new GaugeConfiguration { LabelNames = new[] { "status", "monitored" } }
        );

        private static readonly Gauge SeriesByTypeGauge = Metrics.CreateGauge(
            "sonarr_series_by_type",
            "Series count by type",
            new GaugeConfiguration { LabelNames = new[] { "type" } }
        );

        // Constructor injection - DryIoc handles this automatically
        public SeriesMetricsCollector(ISeriesService seriesService, Logger logger)
            : base(logger)
        {
            _seriesService = seriesService;

            try
            {
                // Initialize gauges on startup
                UpdateGauges();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize series metrics on startup");
            }
        }

        // Event handlers - AUTOMATICALLY called by EventAggregator!
        public void Handle(SeriesAddedEvent message)
        {
            HandleEvent(message, nameof(SeriesAddedEvent), _ =>
            {
                SeriesAddedCounter.Inc();
                UpdateGauges();
            });
        }

        public void Handle(SeriesDeletedEvent message)
        {
            HandleEvent(message, nameof(SeriesDeletedEvent), _ =>
            {
                SeriesDeletedCounter.Inc();
                UpdateGauges();
            });
        }

        public void Handle(SeriesUpdatedEvent message)
        {
            HandleEvent(message, nameof(SeriesUpdatedEvent), _ =>
            {
                // Only update gauges if status or monitoring changed
                // For now, skip to avoid unnecessary DB queries on every update
            });
        }

        // Called by background task periodically
        public override void UpdateGauges()
        {
            if (!ShouldUpdate())
            {
                return;
            }

            try
            {
                var allSeries = _seriesService.GetAllSeries();

                // Update series by status and monitored state - reset all enum combinations to 0 first
                var statusMonitoredCounts = allSeries
                    .GroupBy(s => new { Status = s.Status.ToString(), Monitored = s.Monitored.ToString() })
                    .ToDictionary(g => (g.Key.Status, g.Key.Monitored), g => g.Count());

                foreach (SeriesStatusType status in Enum.GetValues(typeof(SeriesStatusType)))
                {
                    foreach (var monitored in new[] { "True", "False" })
                    {
                        var statusStr = status.ToString();
                        var count = statusMonitoredCounts.TryGetValue((statusStr, monitored), out var c) ? c : 0;
                        SeriesTotalGauge.WithLabels(statusStr, monitored).Set(count);
                    }
                }

                // Update series by type - reset all enum values to 0 first
                var typeCounts = allSeries.GroupBy(s => s.SeriesType.ToString())
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (SeriesTypes seriesType in Enum.GetValues(typeof(SeriesTypes)))
                {
                    var typeKey = seriesType.ToString();
                    var count = typeCounts.TryGetValue(typeKey, out var tc) ? tc : 0;
                    SeriesByTypeGauge.WithLabels(typeKey).Set(count);
                }

                _logger.Trace("Updated series metrics: {0} total series", allSeries.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update series gauge metrics");
            }
        }
    }
}
