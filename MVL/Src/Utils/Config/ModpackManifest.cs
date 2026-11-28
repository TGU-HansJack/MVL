using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CSemVer;
using MVL.Utils.Game;
using MVL.Utils.Help;

namespace MVL.Utils.Config;

public sealed record ModpackMod(string ModId, string Version, string? Name) {
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Pending { get; init; }
}

public class ModpackManifest {
	private const string Prefix = "mvl://modpack/v1/";
	private static readonly HttpClient HttpClient = new();

	public LocalizedString ModpackName { get; set; } = LocalizedString.Empty;
	public GameVersion? GameVersion { get; set; }
	public SVersion ModpackVersion { get; set; } = SVersion.ZeroVersion;
	public List<string> ModpackAuthors { get; set; } = [];
	public List<string> ModpackTags { get; set; } = [];
	public LocalizedString ModpackSummary { get; set; } = LocalizedString.Empty;
	public LocalizedString ModpackDescription { get; set; } = LocalizedString.Empty;
	public string ModpackWebsite { get; set; } = string.Empty;

	[JsonPropertyName("mods")]
	public ModpackMod[] ModList { get; set; } = [];

	public ModpackManifest() { }

	protected ModpackManifest(ModpackManifest source) {
		ModpackName = source.ModpackName;
		GameVersion = source.GameVersion;
		ModpackVersion = source.ModpackVersion;
		ModpackAuthors = source.ModpackAuthors;
		ModpackTags = source.ModpackTags;
		ModpackSummary = source.ModpackSummary;
		ModpackDescription = source.ModpackDescription;
		ModpackWebsite = source.ModpackWebsite;
	}

	public string CreateLink(IEnumerable<ModpackMod> selected) {
		return Prefix + Base64Url.EncodeToString(WriteJson(selected));
	}

	public byte[] WriteJson(IEnumerable<ModpackMod> selected) {
		var manifest = WithSelected(selected);
		var type = SourceGenerationContext.Default.ModpackManifest;
		using var buffer = new MemoryStream();
		using (var writer = new Utf8JsonWriter(buffer, new() { Encoder = type.Options.Encoder, Indented = true })) {
			LocalizedStringJsonHelper.WriteWithOrderedLocalizations(manifest, writer, type);
		}
		return buffer.ToArray();
	}

	private ModpackManifest WithSelected(IEnumerable<ModpackMod> selected) {
		return new ModpackManifest(this) {
			ModList = selected.Select(mod => mod with { Pending = false }).ToArray()
		};
	}

	public static ModpackManifest? ReadLink(string text, out string error) {
		text = text.Trim();
		error = "请输入整合包分享链接";
		if (!text.StartsWith(Prefix, StringComparison.Ordinal)) {
			if (text.StartsWith("mvl://modpack/", StringComparison.Ordinal)) error = "不支持此分享链接版本";
			return null;
		}

		error = "整合包分享链接已损坏";
		try {
			return ReadJson(Base64Url.DecodeFromChars(text.AsSpan(Prefix.Length)), out error);
		} catch (Exception e) when (e is JsonException or FormatException or ArgumentException or InvalidOperationException) {
			return null;
		}
	}

	public static ModpackManifest? ReadJson(ReadOnlySpan<byte> bytes, out string error) {
		error = "modpack.json 内容无效";
		try {
			using var doc = JsonDocument.Parse(bytes.ToArray());
			var root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("mods", out _)) return null;
			var type = SourceGenerationContext.Default.ModpackManifest;
			var manifest = root.Deserialize(type);
			if (manifest is null || manifest.ModList is null || manifest.ModpackVersion is null ||
				manifest.ModpackAuthors is null || manifest.ModpackTags is null) return null;
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var mod in manifest.ModList) {
				if (mod is null || string.IsNullOrWhiteSpace(mod.ModId) || string.IsNullOrWhiteSpace(mod.Version) ||
					!ids.Add(mod.ModId)) return null;
			}
			var fields = root.EnumerateObject().Where(p => p.Name.EndsWith(']')).ToDictionary(p => p.Name, p => p.Value);
			LocalizedStringJsonHelper.RestoreLocalizationsFromExtensionData(manifest, type, fields);
			if (string.IsNullOrWhiteSpace(manifest.ModpackName.Value)) return null;
			error = string.Empty;
			return manifest;
		} catch (Exception e) when (e is JsonException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException) {
			return null;
		}
	}

	public static ModpackManifest? ReadJson(string json, out string error) =>
		ReadJson(Encoding.UTF8.GetBytes(json), out error);

	public static async Task<(ModpackManifest? Manifest, string Error)> ReadSourceAsync(string source) {
		const string defaultError = "请输入整合包链接或 modpack.json 文件";
		source = source.Trim();
		if (source.Length == 0) return (null, defaultError);
		if (source.StartsWith("{") || source.StartsWith("[")) {
			var manifest = ReadJson(source, out var error);
			return (manifest, error);
		}
		if (source.StartsWith(Prefix, StringComparison.Ordinal)) {
			var manifest = ReadLink(source, out var error);
			return (manifest, error);
		}
		try {
			byte[] bytes;
			if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
				(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) {
				bytes = await HttpClient.GetByteArrayAsync(uri);
			} else if (File.Exists(source)) {
				bytes = await File.ReadAllBytesAsync(source);
			} else {
				return (null, "请输入有效的 http(s)://modpack.json 地址或本地文件路径");
			}
			var manifest = ReadJson(bytes, out var error);
			return (manifest, error);
		} catch (HttpRequestException e) {
			return (null, $"下载 modpack.json 失败: {e.Message}");
		} catch (IOException e) {
			return (null, $"读取 modpack.json 失败: {e.Message}");
		}
	}
}
