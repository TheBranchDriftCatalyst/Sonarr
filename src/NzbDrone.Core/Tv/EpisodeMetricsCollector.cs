using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Prometheus;
using NzbDrone.Core.Instrumentation.Metrics;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    /// <summary>
    /// Collects Prometheus metrics for the Episode domain.
    /// Automatically discovered and registered by DryIoc.
    /// </summary>
    public class EpisodeMetricsCollector : MetricsCollectorBase,
        IHandle<EpisodeImportedEvent>,
        IHandle<EpisodeFileDeletedEvent>,
        IHandle<EpisodeInfoRefreshedEvent>
    {
        private readonly IEpisodeService _episodeService;
        private readonly ISeriesService _seriesService;

        // Debounce UpdateGauges to prevent rapid DB queries during batch imports
        protected override TimeSpan DebounceInterval => TimeSpan.FromSeconds(5);

        // Counters (cumulative)
        private static readonly Counter EpisodesImportedCounter = Metrics.CreateCounter(
            "sonarr_episodes_imported_total",
            "Total number of episodes imported",
            new CounterConfiguration { LabelNames = new[] { "source" } }
        );

        private static readonly Counter EpisodesDeletedCounter = Metrics.CreateCounter(
            "sonarr_episodes_deleted_total",
            "Total number of episode files deleted"
        );

        // Gauges (current state)
        private static readonly Gauge EpisodesMissingGauge = Metrics.CreateGauge(
            "sonarr_episodes_missing_total",
            "Number of missing episodes (monitored, aired, no file)"
        );

        private static readonly Gauge EpisodesWithFilesGauge = Metrics.CreateGauge(
            "sonarr_episodes_with_files_total",
            "Number of episodes with files"
        );

        private static readonly Gauge EpisodesTotalGauge = Metrics.CreateGauge(
            "sonarr_episodes_total",
            "Total number of episodes",
            new GaugeConfiguration { LabelNames = new[] { "monitored" } }
        );

        public EpisodeMetricsCollector(IEpisodeService episodeService, ISeriesService seriesService, Logger logger)
            : base(logger)
        {
            _episodeService = episodeService;
            _seriesService = seriesService;

            try
            {
                UpdateGauges();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize episode metrics on startup");
            }
        }

        public void Handle(EpisodeImportedEvent message)
        {
            HandleEvent(message, nameof(EpisodeImportedEvent), msg =>
            {
                // Determine source based on NewDownload flag
                var source = msg.NewDownload ? "download" : "manual";
                EpisodesImportedCounter.WithLabels(source).Inc();
                UpdateGauges();
            });
        }

        public void Handle(EpisodeFileDeletedEvent message)
        {
            HandleEvent(message, nameof(EpisodeFileDeletedEvent), _ =>
            {
                EpisodesDeletedCounter.Inc();
                UpdateGauges();
            });
        }

        public void Handle(EpisodeInfoRefreshedEvent message)
        {
            HandleEvent(message, nameof(EpisodeInfoRefreshedEvent), _ =>
            {
                // Episode info was updated (includes monitor/unmonitor changes)
                UpdateGauges();
            });
        }

        public override void UpdateGauges()
        {
            if (!ShouldUpdate())
            {
                return;
            }

            try
            {
                // Get all series IDs and then fetch all episodes
                var seriesIds = _seriesService.GetAllSeries().Select(s => s.Id).ToList();
                var allEpisodes = _episodeService.GetEpisodesBySeries(seriesIds);

                // Episodes with files
                var withFiles = allEpisodes.Count(e => e.HasFile);
                EpisodesWithFilesGauge.Set(withFiles);

                // Missing episodes (monitored, aired, no file)
                var missing = allEpisodes.Count(e =>
                    e.Monitored &&
                    e.AirDateUtc.HasValue &&
                    e.AirDateUtc.Value < DateTime.UtcNow &&
                    !e.HasFile);
                EpisodesMissingGauge.Set(missing);

                // Total episodes by monitored state - reset both labels to handle cases where one group becomes empty
                var monitoredCounts = allEpisodes.GroupBy(e => e.Monitored)
                    .ToDictionary(g => g.Key, g => g.Count());

                var monitoredCount = monitoredCounts.TryGetValue(true, out var mc) ? mc : 0;
                var unmonitoredCount = monitoredCounts.TryGetValue(false, out var uc) ? uc : 0;

                EpisodesTotalGauge.WithLabels("True").Set(monitoredCount);
                EpisodesTotalGauge.WithLabels("False").Set(unmonitoredCount);

                _logger.Trace("Updated episode metrics: {0} total, {1} with files, {2} missing", allEpisodes.Count, withFiles, missing);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update episode gauge metrics");
            }
        }
    }
}
