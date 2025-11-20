using System;
using System.Linq;
using NLog;
using Prometheus;
using NzbDrone.Core.Instrumentation.Metrics;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.ImportLists
{
    /// <summary>
    /// Collects Prometheus metrics for the ImportList domain.
    /// Automatically discovered and registered by DryIoc.
    /// </summary>
    public class ImportListMetricsCollector : MetricsCollectorBase,
        IHandle<ProviderAddedEvent<ImportListDefinition>>,
        IHandle<ProviderDeletedEvent<ImportListDefinition>>,
        IHandle<ProviderUpdatedEvent<ImportListDefinition>>,
        IHandle<ProviderStatusChangedEvent<IImportList>>
    {
        private readonly IImportListFactory _importListFactory;
        private readonly IImportListStatusService _importListStatusService;

        // Gauges (current state)
        private static readonly Gauge ImportListsTotalGauge = Metrics.CreateGauge(
            "sonarr_import_lists_total",
            "Import lists configured",
            new GaugeConfiguration { LabelNames = new[] { "enabled" } }
        );

        private static readonly Gauge ImportListStatusGauge = Metrics.CreateGauge(
            "sonarr_import_list_status",
            "Import list status (0=available, 1=unavailable)",
            new GaugeConfiguration { LabelNames = new[] { "list_name" } }
        );

        private static readonly Gauge ImportListAutoAddEnabledGauge = Metrics.CreateGauge(
            "sonarr_import_list_auto_add_enabled",
            "Import lists with automatic add enabled"
        );

        // Counters (cumulative)
        private static readonly Counter ImportListsAddedCounter = Metrics.CreateCounter(
            "sonarr_import_lists_added_total",
            "Import lists added"
        );

        private static readonly Counter ImportListsDeletedCounter = Metrics.CreateCounter(
            "sonarr_import_lists_deleted_total",
            "Import lists deleted"
        );

        public ImportListMetricsCollector(IImportListFactory importListFactory, IImportListStatusService importListStatusService, Logger logger)
            : base(logger)
        {
            _importListFactory = importListFactory;
            _importListStatusService = importListStatusService;

            try
            {
                UpdateGauges();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize import list metrics on startup");
            }
        }

        public void Handle(ProviderAddedEvent<ImportListDefinition> message)
        {
            HandleEvent(message, nameof(ProviderAddedEvent<ImportListDefinition>), _ =>
            {
                ImportListsAddedCounter.Inc();
                UpdateGauges();
            });
        }

        public void Handle(ProviderDeletedEvent<ImportListDefinition> message)
        {
            HandleEvent(message, nameof(ProviderDeletedEvent<ImportListDefinition>), _ =>
            {
                ImportListsDeletedCounter.Inc();
                UpdateGauges();
            });
        }

        public void Handle(ProviderUpdatedEvent<ImportListDefinition> message)
        {
            HandleEvent(message, nameof(ProviderUpdatedEvent<ImportListDefinition>), _ => UpdateGauges());
        }

        public void Handle(ProviderStatusChangedEvent<IImportList> message)
        {
            HandleEvent(message, nameof(ProviderStatusChangedEvent<IImportList>), _ => UpdateGauges());
        }

        public override void UpdateGauges()
        {
            try
            {
                var allImportLists = _importListFactory.All();

                // Total import lists (enabled vs disabled)
                var enabled = allImportLists.Count(l => l.Enable);
                var disabled = allImportLists.Count - enabled;
                ImportListsTotalGauge.WithLabels("true").Set(enabled);
                ImportListsTotalGauge.WithLabels("false").Set(disabled);

                // Auto-add enabled
                var autoAddEnabled = allImportLists.Count(l => l.EnableAutomaticAdd);
                ImportListAutoAddEnabledGauge.Set(autoAddEnabled);

                // Import list status (blocked/unavailable)
                var blockedProviders = _importListStatusService.GetBlockedProviders();
                foreach (var importList in allImportLists)
                {
                    var isBlocked = blockedProviders.Any(b => b.ProviderId == importList.Id);
                    var sanitizedName = MetricLabelSanitizer.SanitizeProviderName(importList.Name);
                    ImportListStatusGauge.WithLabels(sanitizedName).Set(isBlocked ? 1 : 0);
                }

                _logger.Trace("Updated import list metrics: {0} total, {1} enabled, {2} auto-add enabled",
                    allImportLists.Count, enabled, autoAddEnabled);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update import list gauge metrics");
            }
        }
    }
}
