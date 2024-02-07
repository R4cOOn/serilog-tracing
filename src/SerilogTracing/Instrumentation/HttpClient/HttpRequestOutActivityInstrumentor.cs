﻿using System.Diagnostics;
using Serilog.Events;
using Serilog.Parsing;

namespace SerilogTracing.Instrumentation.HttpClient;

/// <summary>
/// An activity instrumentor that populates the current activity with context from outgoing HTTP requests.
/// </summary>
sealed class HttpRequestOutActivityInstrumentor: IActivityInstrumentor
{
    readonly PropertyAccessor<HttpRequestMessage> _requestAccessor = new("Request");
    readonly PropertyAccessor<TaskStatus> _requestTaskStatusAccessor = new("RequestTaskStatus");
    readonly PropertyAccessor<HttpResponseMessage> _responseAccessor = new("Response");

    static readonly MessageTemplate MessageTemplateOverride =
        new MessageTemplateParser().Parse("HTTP {RequestMethod} {RequestUri}");

    /// <inheritdoc cref="IActivityInstrumentor.ShouldSubscribeTo"/>
    public bool ShouldSubscribeTo(string diagnosticListenerName)
    {
        return diagnosticListenerName == "HttpHandlerDiagnosticListener";
    }

    /// <inheritdoc cref="IActivityInstrumentor.ShouldSubscribeTo"/>
    public void InstrumentActivity(Activity activity, string eventName, object eventArgs)
    {
        switch (eventName)
        {
            case "System.Net.Http.HttpRequestOut.Start" when
                activity.OperationName == "System.Net.Http.HttpRequestOut":
            {
                if (!_requestAccessor.TryGetValue(eventArgs, out var request) ||
                    request?.RequestUri == null)
                {
                    return;
                }

                // The message template and properties will need to be set through a configurable enrichment
                // mechanism, since the detail/information-leakage trade-off will be different for different
                // consumers.
            
                // For now, stripping any user, query, and fragment should be a reasonable default.

                var uriBuilder = new UriBuilder(request.RequestUri)
                {
                    Query = null!,
                    Fragment = null!,
                    UserName = null!,
                    Password = null!
                };

                ActivityInstrumentation.SetMessageTemplateOverride(activity, MessageTemplateOverride);
                activity.DisplayName = MessageTemplateOverride.Text;
                activity.AddTag("RequestUri", uriBuilder.Uri);
                activity.AddTag("RequestMethod", request.Method);
                break;
            }
            case "System.Net.Http.HttpRequestOut.Stop":
            {
                if (!_responseAccessor.TryGetValue(eventArgs, out var response))
                {
                    return;
                }

                var statusCode = response != null ? (int?)response.StatusCode : null;
                ActivityInstrumentation.SetLogEventProperty(activity, new LogEventProperty("StatusCode", new ScalarValue(statusCode)));

                if (activity.Status == ActivityStatusCode.Unset)
                {
                    if (statusCode >= 400)
                    {
                        activity.SetStatus(ActivityStatusCode.Error);
                    }
                    else if (_requestTaskStatusAccessor.TryGetValue(eventArgs, out var requestTaskStatus))
                    {
                        if (requestTaskStatus == TaskStatus.Faulted || response is { IsSuccessStatusCode: false })
                            activity.SetStatus(ActivityStatusCode.Error);
                    }
                }

                break;
            }
        }
    }
}