namespace NzbDrone.Core.Instrumentation.Metrics
{
    /// <summary>
    /// Implement this interface to provide Prometheus metrics that should be updated periodically.
    /// All implementations are automatically discovered by DryIoc and called by UpdateMetricsService.
    /// </summary>
    public interface IProvideMetrics
    {
        /// <summary>
        /// Update all gauge metrics to reflect current state.
        /// Called periodically by the UpdateMetricsCommand scheduled task.
        /// </summary>
        void UpdateGauges();
    }
}
