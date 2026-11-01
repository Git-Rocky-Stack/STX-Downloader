namespace Stx.Core.Jobs;

public enum JobState { Queued, Running, Paused, Waiting, Analyzing, Completed, Failed, Canceled, NeedsRecapture }
