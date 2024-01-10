﻿using System.Diagnostics;

namespace SerilogTracing.Instrumentation;

/// <summary>
/// 
/// </summary>
public interface IActivityEnricher
{
    /// <summary>
    /// Whether the enricher should subscribe to events from the given <see cref="DiagnosticListener"/>.
    /// </summary>
    /// <param name="listenerName">The <see cref="DiagnosticListener.Name"/> of the candidate <see cref="DiagnosticListener"/>.</param>
    /// <returns>Whether the enricher should receive events from the given listener.</returns>
    bool ShouldListenTo(string listenerName);
    
    /// <summary>
    /// Enrich the an activity with context from a diagnostic event.
    /// </summary>
    /// <param name="activity">The activity to enrich.</param>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="eventArgs">The value of the event.</param>
    void EnrichActivity(Activity activity, string eventName, object eventArgs);
}
