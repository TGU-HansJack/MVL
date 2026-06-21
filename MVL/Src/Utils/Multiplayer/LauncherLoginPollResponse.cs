using System;

namespace MVL.Utils.Multiplayer;

public record struct LauncherLoginPollResponse {
	public string Status { get; set; }
	public string Username { get; set; }
	public string SessionToken { get; set; }
	public DateTimeOffset? ExpiresAt { get; set; }
}
