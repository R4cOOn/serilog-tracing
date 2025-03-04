﻿// Copyright © SerilogTracing Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Diagnostics;
using Serilog;
using Serilog.Events;
using SerilogTracing.Core;
using SerilogTracing.Instrumentation;
using Constants = Serilog.Core.Constants;

namespace SerilogTracing.Interop;

sealed class LoggerActivityListener: IDisposable
{
    readonly ActivityListener? _listener;
    readonly IDisposable? _diagnosticListenerSubscription;

    LoggerActivityListener(ActivityListener? listener, IDisposable? subscription)
    {
        _listener = listener;
        _diagnosticListenerSubscription = subscription;
    }
    
    internal static LoggerActivityListener Configure(ActivityListenerConfiguration configuration, Func<ILogger> logger)
    {
        ILogger GetLogger(string name)
        {
            var instance = logger();
            return !string.IsNullOrWhiteSpace(name)
                ? instance.ForContext(Constants.SourceContextPropertyName, name)
                : instance;
        }

        var activityListener = new ActivityListener();
        var subscription = DiagnosticListener.AllListeners.Subscribe(new DiagnosticListenerObserver(configuration.Instrument.GetInstrumentors().ToArray()));

        try
        {
            var levelMap = configuration.InitialLevel.GetOverrideMap();

            // We may want an opt-in to performing level checks eagerly here.
            // It would be a performance win, but would also prevent dynamic log level changes from being effective.
            activityListener.ShouldListenTo = _ => true;

            var sample = configuration.Sample.ActivityContext;
            activityListener.Sample = (ref ActivityCreationOptions<ActivityContext> activity) =>
            {
                if (!GetLogger(activity.Source.Name)
                        .IsEnabled(GetInitialLevel(levelMap, activity.Source.Name)))
                    return ActivitySamplingResult.None;

                return sample?.Invoke(ref activity) ?? ActivitySamplingResult.AllDataAndRecorded;
            };

            activityListener.ActivityStopped += activity =>
            {
                if (ActivityInstrumentation.IsDataSuppressed(activity)) return;

                if (ActivityInstrumentation.HasAttachedLoggerActivity(activity))
                    return; // `LoggerActivity` completion writes these to the activity-specific logger.

                var activityLogger = GetLogger(activity.Source.Name);

                var level = GetCompletionLevel(levelMap, activity);

                if (!activityLogger.IsEnabled(level))
                    return;

                activityLogger.Write(ActivityConvert.ActivityToLogEvent(activityLogger, activity, level));
            };

            ActivitySource.AddActivityListener(activityListener);

            return new LoggerActivityListener(activityListener, subscription);
        }
        catch
        {
            activityListener.Dispose();
            subscription.Dispose();
            throw;
        }
    }
    
    static LogEventLevel GetInitialLevel(LevelOverrideMap levelMap, string activitySourceName)
    {
        levelMap.GetEffectiveLevel(activitySourceName, out var initialLevel, out var overrideLevel);

        return overrideLevel?.MinimumLevel ?? initialLevel;
    }

    static LogEventLevel GetCompletionLevel(LevelOverrideMap levelMap, Activity activity)
    {
        var level = GetInitialLevel(levelMap, activity.Source.Name);

        if (activity.Status == ActivityStatusCode.Error && level < LogEventLevel.Error)
        {
            return LogEventLevel.Error;
        }

        return level;
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _diagnosticListenerSubscription?.Dispose();
    }
}