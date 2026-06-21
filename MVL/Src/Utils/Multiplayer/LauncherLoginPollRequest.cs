namespace MVL.Utils.Multiplayer;

public record struct LauncherLoginPollRequest {
	public string Id { get; set; }
	public string Secret { get; set; }
}
