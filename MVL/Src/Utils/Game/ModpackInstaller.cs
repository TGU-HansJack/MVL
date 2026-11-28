using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;
using MVL.UI;
using MVL.Utils.Config;

namespace MVL.Utils.Game;

public static class ModpackInstaller {
	public static async Task<((ApiModInfo, ApiModRelease, ModpackConfig)[] Releases, string[] Failures)> ResolveAsync(
		ModpackConfig config,
		Action<int, int, ModpackMod>? progress = null) {
		await config.UpdateModsAsync();
		var mods = config.ModList.Where(mod => mod.Pending).ToArray();
		var releases = new (ApiModInfo, ApiModRelease, ModpackConfig)?[mods.Length];
		var failures = new ConcurrentBag<string>();
		var completed = 0;
		var concurrency = Math.Clamp(Main.BaseConfig.DownloadThreads, 1, 16);
		using var semaphore = new SemaphoreSlim(concurrency);

		await Task.WhenAll(mods.Select(async (mod, index) => {
			await semaphore.WaitAsync();
			try {
				var url = $"https://mods.vintagestory.at/api/mod/{Uri.EscapeDataString(mod.ModId)}";
				await using var stream = await url.GetStreamAsync();
				var status = await JsonSerializer.DeserializeAsync(stream, SourceGenerationContext.Default.ApiStatusModInfo);
				var apiMod = status.Mod;
				var release = apiMod?.Releases.FirstOrDefault(candidate =>
					string.Equals(candidate.ModIdStr, mod.ModId, StringComparison.OrdinalIgnoreCase) &&
					string.Equals(candidate.ModVersion, mod.Version, StringComparison.Ordinal));
				if (status.StatusCode != "200" || apiMod is null || release is null ||
					string.IsNullOrWhiteSpace(release.Value.MainFile)) {
					failures.Add($"{mod.ModId} {mod.Version}: Mod DB 上没有可下载的对应版本");
					return;
				}
				releases[index] = (apiMod.Value, release.Value, config);
			} catch (Exception e) {
				Log.Error($"查询 {mod.ModId} {mod.Version} 失败", e);
				failures.Add($"{mod.ModId} {mod.Version}: {e.Message}");
			} finally {
				progress?.Invoke(Interlocked.Increment(ref completed), mods.Length, mod);
				semaphore.Release();
			}
		}));

		return (releases.Where(release => release.HasValue).Select(release => release!.Value).ToArray(),
			failures.ToArray());
	}
}
