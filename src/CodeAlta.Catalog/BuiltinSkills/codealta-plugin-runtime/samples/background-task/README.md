# Background task sample

Starts a tracked background loop that is cancelled during plugin deactivation. Long-running plugins must use the runtime task service rather than untracked `Task.Run` work.
