using System;
using System.Linq;
using NLog;
using Prometheus;
using NzbDrone.Core.Instrumentation.Metrics;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.HealthCheck
{
    /// <summary>
    /// Collects Prometheus metrics for the HealthCheck domain.
    /// Automatically discovered and registered by DryIoc.
    /// </summary>
    public class HealthCheckMetricsCollector : MetricsCollectorBase,
        IHandle<HealthCheckFailedEvent>,
        IHandle<HealthCheckRestoredEvent>,
        IHandle<HealthCheckCompleteEvent>
    {
        private readonly IHealthCheckService _healthCheckService;

        // Gauges (current state)
        private static readonly Gauge HealthStatusGauge = Metrics.CreateGauge(
            "sonarr_health_status",
            "Overall health status (0=ok, 1=notice, 2=warning, 3=error)",
            new GaugeConfiguration { LabelNames = new[] { "check_name" } }
        );

        private static readonly Gauge HealthChecksByTypeGauge = Metrics.CreateGauge(
            "sonarr_health_checks_by_type",
            "Health checks by result type",
            new GaugeConfiguration { LabelNames = new[] { "type" } }
        );

        private static readonly Gauge HealthIssuesTotalGauge = Metrics.CreateGauge(
            "sonarr_health_issues_total",
            "Number of health issues",
            new GaugeConfiguration { LabelNames = new[] { "severity" } }
        );

        // Counters (cumulative)
        private static readonly Counter HealthFailuresCounter = Metrics.CreateCounter(
            "sonarr_health_failures_total",
            "Health check failures",
            new CounterConfiguration { LabelNames = new[] { "reason" } }
        );

        private static readonly Counter HealthRestoredCounter = Metrics.CreateCounter(
            "sonarr_health_restored_total",
            "Health checks restored",
            new CounterConfiguration { LabelNames = new[] { "reason" } }
        );

        public HealthCheckMetricsCollector(IHealthCheckService healthCheckService, Logger logger)
            : base(logger)
        {
            _healthCheckService = healthCheckService;

            try
            {
                UpdateGauges();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize health check metrics on startup");
            }
        }

        public void Handle(HealthCheckFailedEvent message)
        {
            HandleEventWithNestedCheck(
                message,
                nameof(HealthCheckFailedEvent),
                msg => msg.HealthCheck != null,
                msg =>
                {
                    if (!msg.IsInStartupGracePeriod)
                    {
                        var reason = msg.HealthCheck.Reason.ToString();
                        HealthFailuresCounter.WithLabels(reason).Inc();
                    }

                    UpdateGauges();
                });
        }

        public void Handle(HealthCheckRestoredEvent message)
        {
            HandleEventWithNestedCheck(
                message,
                nameof(HealthCheckRestoredEvent),
                msg => msg.PreviousCheck != null,
                msg =>
                {
                    if (!msg.IsInStartupGracePeriod)
                    {
                        var reason = msg.PreviousCheck.Reason.ToString();
                        HealthRestoredCounter.WithLabels(reason).Inc();
                    }

                    UpdateGauges();
                });
        }

        public void Handle(HealthCheckCompleteEvent message)
        {
            HandleEvent(message, nameof(HealthCheckCompleteEvent), _ => UpdateGauges());
        }

        public override void UpdateGauges()
        {
            try
            {
                var results = _healthCheckService.Results();

                // Health status per check
                foreach (var check in results)
                {
                    var checkName = check.Source?.Name ?? "unknown";
                    var statusValue = (int)check.Type;
                    HealthStatusGauge.WithLabels(checkName).Set(statusValue);
                }

                // Health checks by type - reset all enum values to 0 first to clean up old labels
                var typeCounts = results.GroupBy(h => h.Type.ToString().ToLower())
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (HealthCheckResult resultType in Enum.GetValues(typeof(HealthCheckResult)))
                {
                    var typeKey = resultType.ToString().ToLower();
                    var count = typeCounts.TryGetValue(typeKey, out var tc) ? tc : 0;
                    HealthChecksByTypeGauge.WithLabels(typeKey).Set(count);
                }

                // Health issues by severity
                var notice = results.Count(h => h.Type == HealthCheckResult.Notice);
                var warning = results.Count(h => h.Type == HealthCheckResult.Warning);
                var error = results.Count(h => h.Type == HealthCheckResult.Error);

                HealthIssuesTotalGauge.WithLabels("notice").Set(notice);
                HealthIssuesTotalGauge.WithLabels("warning").Set(warning);
                HealthIssuesTotalGauge.WithLabels("error").Set(error);

                _logger.Trace("Updated health check metrics: {0} notice, {1} warning, {2} error",
                    notice, warning, error);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update health check gauge metrics");
            }
        }
    }
}
