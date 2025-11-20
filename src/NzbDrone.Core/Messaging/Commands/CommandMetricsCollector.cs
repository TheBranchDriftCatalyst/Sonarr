using System;
using System.Linq;
using NLog;
using Prometheus;
using NzbDrone.Core.Instrumentation.Metrics;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Messaging.Commands
{
    /// <summary>
    /// Collects Prometheus metrics for Command execution.
    /// Automatically discovered and registered by DryIoc.
    /// </summary>
    public class CommandMetricsCollector : MetricsCollectorBase,
        IHandle<CommandExecutedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;

        // Gauges (current state)
        private static readonly Gauge CommandsActiveGauge = Metrics.CreateGauge(
            "sonarr_commands_active",
            "Active commands",
            new GaugeConfiguration { LabelNames = new[] { "command_name" } }
        );

        private static readonly Gauge CommandsQueuedGauge = Metrics.CreateGauge(
            "sonarr_commands_queued",
            "Queued commands",
            new GaugeConfiguration { LabelNames = new[] { "command_name", "priority" } }
        );

        private static readonly Gauge CommandQueueDepthGauge = Metrics.CreateGauge(
            "sonarr_command_queue_depth",
            "Command queue depth by priority",
            new GaugeConfiguration { LabelNames = new[] { "priority" } }
        );

        // Counters (cumulative)
        private static readonly Counter CommandsExecutedCounter = Metrics.CreateCounter(
            "sonarr_commands_executed_total",
            "Commands executed",
            new CounterConfiguration { LabelNames = new[] { "command_name", "result" } }
        );

        private static readonly Counter CommandsFailedCounter = Metrics.CreateCounter(
            "sonarr_commands_failed_total",
            "Command failures",
            new CounterConfiguration { LabelNames = new[] { "command_name" } }
        );

        // Histograms (distributions)
        private static readonly Histogram CommandDurationHistogram = Metrics.CreateHistogram(
            "sonarr_command_duration_seconds",
            "Command execution duration",
            new HistogramConfiguration
            {
                LabelNames = new[] { "command_name" },
                Buckets = new[] { 1.0, 5.0, 10.0, 30.0, 60.0, 300.0, 600.0, 1800.0, 3600.0 }
            }
        );

        public CommandMetricsCollector(IManageCommandQueue commandQueueManager, Logger logger)
            : base(logger)
        {
            _commandQueueManager = commandQueueManager;

            try
            {
                UpdateGauges();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize command metrics on startup");
            }
        }

        public void Handle(CommandExecutedEvent message)
        {
            HandleEventWithNestedCheck(
                message,
                nameof(CommandExecutedEvent),
                msg => msg.Command != null,
                msg =>
                {
                    var commandName = MetricLabelSanitizer.SanitizeCommandName(msg.Command.Name);
                    var result = msg.Command.Status.ToString().ToLower();

                    // Track execution
                    CommandsExecutedCounter.WithLabels(commandName, result).Inc();

                    // Track failures
                    if (msg.Command.Status == CommandStatus.Failed)
                    {
                        CommandsFailedCounter.WithLabels(commandName).Inc();
                    }

                    // Track duration
                    if (msg.Command.Duration.HasValue)
                    {
                        CommandDurationHistogram
                            .WithLabels(commandName)
                            .Observe(msg.Command.Duration.Value.TotalSeconds);
                    }

                    UpdateGauges();

                    _logger.Trace("Command executed: {0}, status={1}, duration={2}s",
                        commandName, result, msg.Command.Duration?.TotalSeconds ?? 0);
                });
        }

        public override void UpdateGauges()
        {
            try
            {
                var allCommands = _commandQueueManager.All();

                // Active commands
                var active = allCommands.Where(c => c.Status == CommandStatus.Started).ToList();
                var byCommandName = active.GroupBy(c => MetricLabelSanitizer.SanitizeCommandName(c.Name));
                foreach (var group in byCommandName)
                {
                    CommandsActiveGauge.WithLabels(group.Key).Set(group.Count());
                }

                // Queued commands
                var queued = allCommands.Where(c => c.Status == CommandStatus.Queued).ToList();
                var byNameAndPriority = queued.GroupBy(c => new
                {
                    Name = MetricLabelSanitizer.SanitizeCommandName(c.Name),
                    c.Priority
                });
                foreach (var group in byNameAndPriority)
                {
                    CommandsQueuedGauge.WithLabels(group.Key.Name, group.Key.Priority.ToString()).Set(group.Count());
                }

                // Queue depth by priority
                var byPriority = queued.GroupBy(c => c.Priority);
                foreach (var group in byPriority)
                {
                    CommandQueueDepthGauge.WithLabels(group.Key.ToString()).Set(group.Count());
                }

                // Ensure we have zero values for all priorities if queue is empty
                if (!queued.Any())
                {
                    CommandQueueDepthGauge.WithLabels("Low").Set(0);
                    CommandQueueDepthGauge.WithLabels("Normal").Set(0);
                    CommandQueueDepthGauge.WithLabels("High").Set(0);
                }

                _logger.Trace("Updated command metrics: {0} active, {1} queued",
                    active.Count, queued.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update command gauge metrics");
            }
        }
    }
}
