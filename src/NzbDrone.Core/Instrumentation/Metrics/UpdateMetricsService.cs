using System;
using System.Collections.Generic;
using System.Diagnostics;
using NLog;
using NzbDrone.Core.Messaging.Commands;
using Prometheus;

namespace NzbDrone.Core.Instrumentation.Metrics
{
    /// <summary>
    /// Scheduled task that updates all Prometheus metric providers.
    /// DryIoc automatically injects ALL implementations of IProvideMetrics!
    /// </summary>
    public class UpdateMetricsService : IExecute<UpdateMetricsCommand>
    {
        private readonly IEnumerable<IProvideMetrics> _metricsProviders;
        private readonly Logger _logger;

        // Meta-metrics for monitoring the metrics system itself
        private static readonly Counter MetricsUpdateCounter = Prometheus.Metrics.CreateCounter(
            "sonarr_metrics_updates_total",
            "Total metrics update operations",
            new CounterConfiguration { LabelNames = new[] { "status" } }
        );

        private static readonly Counter MetricsCollectorErrorsCounter = Prometheus.Metrics.CreateCounter(
            "sonarr_metrics_collector_errors_total",
            "Errors by metric collector",
            new CounterConfiguration { LabelNames = new[] { "collector" } }
        );

        private static readonly Gauge MetricsCollectorsGauge = Prometheus.Metrics.CreateGauge(
            "sonarr_metrics_collectors_total",
            "Number of registered metric collectors"
        );

        private static readonly Histogram MetricsUpdateDurationHistogram = Prometheus.Metrics.CreateHistogram(
            "sonarr_metrics_update_duration_seconds",
            "Time taken to update all metrics",
            new HistogramConfiguration
            {
                Buckets = new[] { 0.01, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0 }
            }
        );

        private static readonly Gauge MetricsLastUpdateTimestampGauge = Prometheus.Metrics.CreateGauge(
            "sonarr_metrics_last_update_timestamp_seconds",
            "Timestamp of last successful metrics update"
        );

        // DryIoc will automatically inject ALL IProvideMetrics implementations here!
        public UpdateMetricsService(IEnumerable<IProvideMetrics> metricsProviders, Logger logger)
        {
            _metricsProviders = metricsProviders;
            _logger = logger;
        }

        public void Execute(UpdateMetricsCommand message)
        {
            _logger.Debug("Updating Prometheus metrics...");

            var stopwatch = Stopwatch.StartNew();
            var providerCount = 0;
            var errorCount = 0;

            foreach (var provider in _metricsProviders)
            {
                try
                {
                    providerCount++;
                    var collectorName = provider.GetType().Name;
                    _logger.Trace("Updating metrics from {0}", collectorName);
                    provider.UpdateGauges();
                }
                catch (Exception ex)
                {
                    errorCount++;
                    var collectorName = provider.GetType().Name;
                    MetricsCollectorErrorsCounter.WithLabels(collectorName).Inc();
                    _logger.Error(ex, "Failed to update metrics from {0}", collectorName);
                }
            }

            stopwatch.Stop();

            // Update meta-metrics
            MetricsCollectorsGauge.Set(providerCount);
            MetricsUpdateDurationHistogram.Observe(stopwatch.Elapsed.TotalSeconds);

            if (errorCount > 0)
            {
                MetricsUpdateCounter.WithLabels("partial_success").Inc();
                _logger.Warn("Prometheus metrics updated with {0} errors ({1}/{2} providers succeeded) in {3}ms",
                    errorCount, providerCount - errorCount, providerCount, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                MetricsUpdateCounter.WithLabels("success").Inc();
                MetricsLastUpdateTimestampGauge.SetToCurrentTimeUtc();
                _logger.Debug("Prometheus metrics updated successfully from {0} providers in {1}ms",
                    providerCount, stopwatch.ElapsedMilliseconds);
            }
        }
    }
}
