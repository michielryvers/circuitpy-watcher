using System;

namespace Watcher.Config;

public sealed class AppConfig
{
	// Required
	public required string Address { get; init; } // Host or IP, optional :port
	public required string Password { get; init; } // HTTP Basic password (username blank)

	// Optional
	public string LocalRoot { get; init; } = "./CIRCUITPYTHON";
	public int RemotePollIntervalSeconds { get; init; } = 120; // Full-tree poll
	public int WritablePollIntervalSeconds { get; init; } = 5;  // When paused on 409
	public int DebounceMilliseconds { get; init; } = 500;       // Local FS events

	public void Validate()
	{
		if (string.IsNullOrWhiteSpace(Address))
			throw new ArgumentException("Address is required", nameof(Address));
		if (string.IsNullOrWhiteSpace(Password))
			throw new ArgumentException("Password is required", nameof(Password));
		if (RemotePollIntervalSeconds <= 0)
			throw new ArgumentOutOfRangeException(nameof(RemotePollIntervalSeconds));
		if (WritablePollIntervalSeconds <= 0)
			throw new ArgumentOutOfRangeException(nameof(WritablePollIntervalSeconds));
		if (DebounceMilliseconds < 0)
			throw new ArgumentOutOfRangeException(nameof(DebounceMilliseconds));
	}
}
