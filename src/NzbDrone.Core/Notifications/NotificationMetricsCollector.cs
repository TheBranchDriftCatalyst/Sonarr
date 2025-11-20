using System;
using System.Linq;
using NLog;
using Prometheus;
using NzbDrone.Core.Instrumentation.Metrics;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Notifications
{
    /// <summary>
    /// Collects Prometheus metrics for the Notification domain.
    /// Automatically discovered and registered by DryIoc.
    /// </summary>
    public class NotificationMetricsCollector : MetricsCollectorBase,
        IHandle<ProviderAddedEvent<NotificationDefinition>>,
        IHandle<ProviderDeletedEvent<NotificationDefinition>>,
        IHandle<ProviderUpdatedEvent<NotificationDefinition>>,
        IHandle<ProviderStatusChangedEvent<INotification>>
    {
        private readonly INotificationFactory _notificationFactory;
        private readonly INotificationStatusService _notificationStatusService;

        // Gauges (current state)
        private static readonly Gauge NotificationsTotalGauge = Metrics.CreateGauge(
            "sonarr_notifications_total",
            "Total number of notifications configured",
            new GaugeConfiguration { LabelNames = new[] { "enabled" } }
        );

        private static readonly Gauge NotificationsByTypeGauge = Metrics.CreateGauge(
            "sonarr_notifications_by_type",
            "Notifications by type",
            new GaugeConfiguration { LabelNames = new[] { "notification_type", "event_type" } }
        );

        private static readonly Gauge NotificationStatusGauge = Metrics.CreateGauge(
            "sonarr_notification_status",
            "Notification status (0=available, 1=unavailable)",
            new GaugeConfiguration { LabelNames = new[] { "notification" } }
        );

        // Counters (cumulative)
        private static readonly Counter NotificationsAddedCounter = Metrics.CreateCounter(
            "sonarr_notifications_added_total",
            "Notifications added"
        );

        private static readonly Counter NotificationsDeletedCounter = Metrics.CreateCounter(
            "sonarr_notifications_deleted_total",
            "Notifications deleted"
        );

        public NotificationMetricsCollector(INotificationFactory notificationFactory, INotificationStatusService notificationStatusService, Logger logger)
            : base(logger)
        {
            _notificationFactory = notificationFactory;
            _notificationStatusService = notificationStatusService;

            try
            {
                UpdateGauges();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize notification metrics on startup");
            }
        }

        public void Handle(ProviderAddedEvent<NotificationDefinition> message)
        {
            HandleEvent(message, nameof(ProviderAddedEvent<NotificationDefinition>), _ =>
            {
                NotificationsAddedCounter.Inc();
                UpdateGauges();
            });
        }

        public void Handle(ProviderDeletedEvent<NotificationDefinition> message)
        {
            HandleEvent(message, nameof(ProviderDeletedEvent<NotificationDefinition>), _ =>
            {
                NotificationsDeletedCounter.Inc();
                UpdateGauges();
            });
        }

        public void Handle(ProviderUpdatedEvent<NotificationDefinition> message)
        {
            HandleEvent(message, nameof(ProviderUpdatedEvent<NotificationDefinition>), _ => UpdateGauges());
        }

        public void Handle(ProviderStatusChangedEvent<INotification> message)
        {
            HandleEvent(message, nameof(ProviderStatusChangedEvent<INotification>), _ => UpdateGauges());
        }

        public override void UpdateGauges()
        {
            try
            {
                var allNotifications = _notificationFactory.All();

                // Total notifications (enabled vs disabled)
                var enabled = allNotifications.Count(n => n.Enable);
                var disabled = allNotifications.Count - enabled;
                NotificationsTotalGauge.WithLabels("true").Set(enabled);
                NotificationsTotalGauge.WithLabels("false").Set(disabled);

                // Notifications by event type
                var onGrab = allNotifications.Count(n => n.OnGrab);
                var onDownload = allNotifications.Count(n => n.OnDownload);
                var onUpgrade = allNotifications.Count(n => n.OnUpgrade);
                var onRename = allNotifications.Count(n => n.OnRename);
                var onHealthIssue = allNotifications.Count(n => n.OnHealthIssue);

                NotificationsByTypeGauge.WithLabels("all", "grab").Set(onGrab);
                NotificationsByTypeGauge.WithLabels("all", "download").Set(onDownload);
                NotificationsByTypeGauge.WithLabels("all", "upgrade").Set(onUpgrade);
                NotificationsByTypeGauge.WithLabels("all", "rename").Set(onRename);
                NotificationsByTypeGauge.WithLabels("all", "health_issue").Set(onHealthIssue);

                // Notification status (blocked/unavailable)
                var blockedProviders = _notificationStatusService.GetBlockedProviders();
                foreach (var notification in allNotifications)
                {
                    var isBlocked = blockedProviders.Any(b => b.ProviderId == notification.Id);
                    var sanitizedName = MetricLabelSanitizer.SanitizeProviderName(notification.Name);
                    NotificationStatusGauge.WithLabels(sanitizedName).Set(isBlocked ? 1 : 0);
                }

                _logger.Trace("Updated notification metrics: {0} total, {1} enabled",
                    allNotifications.Count, enabled);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update notification gauge metrics");
            }
        }
    }
}
