namespace Stx.Core.Errors;

public enum ErrorKind { UnsupportedSite, SignInNeeded, PrivateOrUnavailable, DrmDetected, LinkExpired, Network, ToolFailure, OutputFailure, CopyFailedCheck, EncoderMissing }

/// <summary>Every error in plain language, with what to do next.</summary>
public static class ErrorKindText
{
    public static string Message(this ErrorKind kind) => kind switch
    {
        ErrorKind.UnsupportedSite => "This link can't be read directly.",
        ErrorKind.SignInNeeded => "This site wants you signed in.",
        ErrorKind.PrivateOrUnavailable => "This item is private, removed or unavailable.",
        ErrorKind.DrmDetected => "This stream is DRM-protected, so it can't be saved.",
        ErrorKind.LinkExpired => "The stream link has expired.",
        ErrorKind.Network => "The connection dropped, or the site isn't responding.",
        ErrorKind.OutputFailure => "The file couldn't be written.",
        ErrorKind.CopyFailedCheck => "The new copy didn't check out, so it wasn't kept. The original is untouched.",
        ErrorKind.EncoderMissing => "This FFmpeg has no encoder for that format.",
        _ => "The download tool stopped unexpectedly.",
    };

    public static string NextStep(this ErrorKind kind) => kind switch
    {
        ErrorKind.UnsupportedSite => "Play it in Chrome and capture it with the extension.",
        ErrorKind.SignInNeeded => "Open it in Chrome, where you're signed in, and send it from the extension.",
        ErrorKind.PrivateOrUnavailable => "Check the link, or ask whoever shared it for access.",
        ErrorKind.DrmDetected => "Nothing more can be done for this item.",
        ErrorKind.LinkExpired => "Play it again in Chrome to capture a fresh link.",
        ErrorKind.Network => "Check your connection, then retry.",
        ErrorKind.OutputFailure => "Check the output folder exists and has free space, then retry.",
        ErrorKind.CopyFailedCheck => "Copy the log to see what didn't match. Retrying makes a new copy from the original.",
        ErrorKind.EncoderMissing => "Let the tools update in Settings, then retry.",
        _ => "Retry, and copy the log if it keeps happening.",
    };
}
