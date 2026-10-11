using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Tropicast.Dashboard.Api.Logging;

/// <summary>
/// Production console logs: one JSON object per line (time, level, category, event, message, structured values,
/// exception type and message), with every string passed through <see cref="Redaction"/>.
/// </summary>
internal sealed class RedactingJsonFormatter() : ConsoleFormatter(Name)
{
    internal new const string Name = "redacting-json";

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
        {
            return;
        }
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("time", DateTimeOffset.UtcNow);
            json.WriteString("level", logEntry.LogLevel.ToString());
            json.WriteString("category", logEntry.Category);
            if (logEntry.EventId.Id != 0)
            {
                json.WriteNumber("eventId", logEntry.EventId.Id);
            }
            json.WriteString("message", Redaction.Apply(message));
            if (logEntry.State is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                json.WriteStartObject("values");
                foreach (var (key, value) in values)
                {
                    if (key == "{OriginalFormat}")
                    {
                        continue;
                    }
                    json.WriteString(key, value is null ? null : Redaction.Apply(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""));
                }
                json.WriteEndObject();
            }
            if (logEntry.Exception is { } exception)
            {
                // Type and message only: stack traces stay out of shipped logs.
                json.WriteString("exception", exception.GetType().FullName);
                json.WriteString("exceptionMessage", Redaction.Apply(exception.Message));
            }
            json.WriteEndObject();
        }
        textWriter.Write(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
        textWriter.Write(Environment.NewLine);
    }
}
