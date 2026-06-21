using System;

namespace MVL.Utils.Multiplayer;

public record struct LauncherLoginStartResponse {
	public string Id { get; set; }
	public string Secret { get; set; }
	public string LoginUrl { get; set; }
	public DateTimeOffset ExpiresAt { get; set; }
}
