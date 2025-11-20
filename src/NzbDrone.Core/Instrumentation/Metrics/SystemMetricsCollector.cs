using System;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using Prometheus;

namespace NzbDrone.Core.Instrumentation.Metrics
{
    /// <summary>
    /// Collects Prometheus metrics for System/Application level information.
    /// Automatically discovered and registered by DryIoc.
    /// </summary>
    public class SystemMetricsCollector : MetricsCollectorBase,
        IHandle<ApplicationStartedEvent>,
        IHandle<ConfigSavedEvent>
    {
        private readonly IRuntimeInfo _runtimeInfo;

        // Info Gauge (version information)
        private static readonly Gauge VersionInfoGauge = Prometheus.Metrics.CreateGauge(
            "sonarr_version_info",
            "Sonarr version info",
            new GaugeConfiguration { LabelNames = new[] { "version", "branch" } }
        );

        // Gauges (current state)
        private static readonly Gauge UptimeSecondsGauge = Prometheus.Metrics.CreateGauge(
            "sonarr_uptime_seconds",
            "Uptime in seconds"
        );

        private static readonly Gauge ConfigVersionGauge = Prometheus.Metrics.CreateGauge(
            "sonarr_config_version",
            "Configuration version (increments on changes)"
        );

        // Counters (cumulative)
        private static readonly Counter AppStartsCounter = Prometheus.Metrics.CreateCounter(
            "sonarr_app_starts_total",
            "Application starts"
        );

        private static readonly Counter ConfigSavedCounter = Prometheus.Metrics.CreateCounter(
            "sonarr_config_saved_total",
            "Configuration changes"
        );

        private static long _configVersion = 0;

        public SystemMetricsCollector(IRuntimeInfo runtimeInfo, Logger logger)
            : base(logger)
        {
            _runtimeInfo = runtimeInfo;

            try
            {
                // Set version info (this is static and won't change during runtime)
                var version = BuildInfo.Version.ToString();
                var branch = BuildInfo.Branch;
                VersionInfoGauge.WithLabels(version, branch).Set(1);

                UpdateGauges();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize system metrics on startup");
            }
        }

        public void Handle(ApplicationStartedEvent message)
        {
            HandleEvent(message, nameof(ApplicationStartedEvent), _ =>
            {
                AppStartsCounter.Inc();

                _logger.Info("Sonarr started - Version: {0}, Branch: {1}",
                    BuildInfo.Version, BuildInfo.Branch);
            });
        }

        public void Handle(ConfigSavedEvent message)
        {
            HandleEvent(message, nameof(ConfigSavedEvent), _ =>
            {
                ConfigSavedCounter.Inc();

                // Thread-safe increment
                var newVersion = System.Threading.Interlocked.Increment(ref _configVersion);
                ConfigVersionGauge.Set(newVersion);

                _logger.Trace("Configuration saved, version: {ConfigVersion}", newVersion);
            });
        }

        public override void UpdateGauges()
        {
            try
            {
                // Calculate uptime
                var uptime = DateTime.UtcNow - _runtimeInfo.StartTime;
                UptimeSecondsGauge.Set(uptime.TotalSeconds);

                _logger.Trace("Updated system metrics: uptime={0}s", uptime.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update system gauge metrics");
            }
        }
    }
}
