using System;
using NLog;

namespace NzbDrone.Core.Instrumentation.Metrics
{
    /// <summary>
    /// Base class for all metric collectors providing common event handling patterns.
    /// Handles null checking, exception handling, and logging consistently across all collectors.
    /// </summary>
    public abstract class MetricsCollectorBase : IProvideMetrics
    {
        protected readonly Logger _logger;

        // Debouncing support for expensive UpdateGauges calls
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private bool _updatePending = false;
        protected virtual TimeSpan DebounceInterval => TimeSpan.Zero; // Override to enable debouncing

        protected MetricsCollectorBase(Logger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Checks if UpdateGauges should be debounced.
        /// Returns true if the update should proceed, false if it should be skipped.
        /// </summary>
        protected bool ShouldUpdate()
        {
            if (DebounceInterval == TimeSpan.Zero)
            {
                return true; // No debouncing
            }

            var now = DateTime.UtcNow;
            var elapsed = now - _lastUpdateTime;

            if (elapsed < DebounceInterval)
            {
                _updatePending = true;
                _logger.Trace("Debouncing UpdateGauges - last update was {0}ms ago", elapsed.TotalMilliseconds);
                return false;
            }

            _lastUpdateTime = now;
            _updatePending = false;
            return true;
        }

        /// <summary>
        /// Returns true if there's a pending update that was debounced.
        /// </summary>
        public bool HasPendingUpdate => _updatePending;

        /// <summary>
        /// Safely handles an event with null checking and exception handling.
        /// </summary>
        /// <typeparam name="TEvent">The event type</typeparam>
        /// <param name="message">The event message</param>
        /// <param name="eventName">The event name for logging</param>
        /// <param name="handler">The handler action to execute</param>
        protected void HandleEvent<TEvent>(TEvent message, string eventName, Action<TEvent> handler)
            where TEvent : class
        {
            if (message == null)
            {
                _logger.Warn($"Received null {eventName}");
                return;
            }

            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to update metrics for {eventName}");
            }
        }

        /// <summary>
        /// Safely handles an event with nested null checking and exception handling.
        /// Useful when the event requires a nested property to be non-null.
        /// </summary>
        /// <typeparam name="TEvent">The event type</typeparam>
        /// <param name="message">The event message</param>
        /// <param name="eventName">The event name for logging</param>
        /// <param name="nestedPropertyCheck">Function to check nested property is non-null</param>
        /// <param name="handler">The handler action to execute</param>
        protected void HandleEventWithNestedCheck<TEvent>(
            TEvent message,
            string eventName,
            Func<TEvent, bool> nestedPropertyCheck,
            Action<TEvent> handler)
            where TEvent : class
        {
            if (message == null || !nestedPropertyCheck(message))
            {
                _logger.Warn($"Received null or invalid {eventName}");
                return;
            }

            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to update metrics for {eventName}");
            }
        }

        /// <summary>
        /// Called periodically to update gauge metrics.
        /// </summary>
        public abstract void UpdateGauges();
    }
}
