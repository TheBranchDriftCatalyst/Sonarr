using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Instrumentation.Metrics
{
    /// <summary>
    /// Command to trigger Prometheus metrics update.
    /// Scheduled to run periodically by TaskManager.
    /// </summary>
    public class UpdateMetricsCommand : Command
    {
        public override bool SendUpdatesToClient => false;
        public override bool IsLongRunning => false;

        public UpdateMetricsCommand()
        {
        }
    }
}
