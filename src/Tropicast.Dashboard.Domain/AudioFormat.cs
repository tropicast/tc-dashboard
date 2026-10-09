namespace Tropicast.Dashboard.Domain;

/// <summary>A stream format, as in the mount <c>/stations/{id}/live.{mp3,opus}</c>.</summary>
public enum AudioFormat
{
    Mp3,
    Opus,
}

/// <summary>Set of stream formats a plan allows.</summary>
[Flags]
public enum AudioFormats
{
    None = 0,
    Mp3 = 1,
    Opus = 2,
}
