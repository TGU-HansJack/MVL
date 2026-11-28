using System;
using System.Threading.Tasks;
using Godot;
using MVL.UI.Item;
using MVL.UI.Window;
using MVL.Utils.Config;
using MVL.Utils.Game;
using MVL.Utils.Help;
using MVL.Utils;

namespace MVL.UI.Page;

public partial class ModpackPage : MenuPage {
	[Export]
	private Button? _addModpackButton;

	[Export]
	private PackedScene? _modpackItemScene;

	[Export]
	private PackedScene? _modpackAddScene;

	[Export]
	private ReorderableContainer? _list;

	private bool _importWorkflowRunning;
	private ApiModReleasesWindow? _activeImportDownloadWindow;

	public override void _Ready() {
		_addModpackButton.NotNull();
		_modpackItemScene.NotNull();
		_modpackAddScene.NotNull();
		_list.NotNull();
		VisibilityChanged += OnVisibilityChanged;
		_addModpackButton.Pressed += () => OpenAddWindow(false);
		GetNode<Button>("%ImportButton").Pressed += () => OpenAddWindow(true);
		_list.Reordered += ListOnReordered;
	}

	private void ListOnReordered(int from, int to) {
		var fromPath = UI.Main.BaseConfig.Modpack[from];
		UI.Main.BaseConfig.Modpack.RemoveAt(from);
		UI.Main.BaseConfig.Modpack.Insert(to, fromPath);
		UI.Main.BaseConfig.Save();
	}

	private void OpenAddWindow(bool import) {
		var addModpack = _modpackAddScene!.Instantiate<AddModpackWindow>();
		addModpack.ImportMode = import;
		addModpack.AddModpack += (name, path, version, release) =>
			AddModpack(name, path, version, release, addModpack.ImportManifest);
		addModpack.Hidden += addModpack.QueueFree;
		UI.Main.Instance?.AddChild(addModpack);
		_ = addModpack.Show();
	}

	private async void AddModpack(string name, string path, string version, string release, ModpackManifest? manifest) {
		if (UI.Main.ModpackConfigs.ContainsKey(path) || (manifest != null && _importWorkflowRunning)) return;
		if (manifest != null) _importWorkflowRunning = true;
		try {
			var config = manifest == null ? new ModpackConfig() : new ModpackConfig(manifest);
			config.Path = path;
			config.ModpackName = config.ModpackName with { Value = name };
			config.GameVersion = new GameVersion(version);
			config.ReleasePath = release;
			config.Save();
			UI.Main.ModpackConfigs[path] = config;
			UI.Main.BaseConfig.Modpack.Insert(0, path);
			if (UI.Main.BaseConfig.Modpack.Count == 1) UI.Main.BaseConfig.CurrentModpack = path;
			await UI.Main.BaseConfig.SaveAsync();
			if (manifest != null) await InstallImportedMods(config);
		} catch (Exception ex) {
			Log.Error(ex);
			await ShowResult(Tr("创建整合包失败") + "\n" + ex.Message);
		} finally {
			if (manifest != null) _importWorkflowRunning = false;
			UpdateList();
		}
	}

	private async Task InstallImportedMods(ModpackConfig config) {
		if (GodotObject.IsInstanceValid(_activeImportDownloadWindow)) return;
		var progressWindow = UI.Main.Instance!.OpenConfirmationWindow("正在查询整合包中的模组版本…");
		progressWindow.TitleLabel!.Text = "导入整合包";
		progressWindow.TitleLabel.Show();
		progressWindow.CancelButton!.Disabled = true;
		progressWindow.OkButton!.Hide();
		progressWindow.Hidden += progressWindow.QueueFree;
		await progressWindow.Show();
		((ApiModInfo, ApiModRelease, ModpackConfig)[] Releases, string[] Failures) result;
		try {
			result = await ModpackInstaller.ResolveAsync(config, (index, total, mod) =>
				Dispatcher.SynchronizationContext.Post(_ => {
						if (GodotObject.IsInstanceValid(progressWindow)) {
							progressWindow.Message = $"{index}/{total}   {mod.ModId}   {mod.Version}";
						}
					}, null));
		} catch (Exception ex) {
			Log.Error(ex);
			result = ([], [ex.Message]);
		}
		await progressWindow.Hide();

		if (result.Releases.Length == 0) {
			var message = result.Failures.Length == 0
				? Tr("整合包中的模组已全部安装")
				: Tr("以下模组没有找到可下载的对应版本") + "\n" + string.Join("\n", result.Failures);
			await ShowResult(message);
			return;
		}

		var downloadWindow = GD.Load<PackedScene>("res://Src/UI/Window/api_mod_releases_window.tscn")
			.Instantiate<ApiModReleasesWindow>();
		downloadWindow.ModDependencies = result.Releases;
		downloadWindow.WindowTitle = "确认下载整合包模组";
		downloadWindow.ConfirmButtonText = "下载所选模组";
		_activeImportDownloadWindow = downloadWindow;
		downloadWindow.TreeExited += () => _activeImportDownloadWindow = null;
		if (result.Failures.Length > 0) {
			downloadWindow.StatusMessage = Tr("以下模组没有找到可下载的对应版本") + "\n" +
				string.Join("\n", result.Failures);
		}
		downloadWindow.Hidden += downloadWindow.QueueFree;
		UI.Main.Instance.AddChild(downloadWindow);
		await downloadWindow.Show();
	}

	private async Task ShowResult(string message) {
		var window = UI.Main.Instance!.OpenConfirmationWindow(message.Replace("[", "[lb]"));
		window.TitleLabel!.Text = "整合包";
		window.CancelButton!.Text = "关闭";
		window.OkButton!.Hide();
		window.Hidden += window.QueueFree;
		await window.Show();
	}

	private void OnVisibilityChanged() {
		UpdateList();
	}

	public async void UpdateList() {
		foreach (var child in _list!.GetChildren()) {
			if (child == _addModpackButton) {
				continue;
			}

			child.QueueFree();
		}

		if (!Visible) {
			return;
		}

		await Task.Run(UI.Main.CheckModpackConfig);
		var i = 1;
		foreach (var path in UI.Main.BaseConfig.Modpack) {
			if (!Visible) {
				break;
			}

			var modpack = UI.Main.ModpackConfigs[path];
			var item = _modpackItemScene!.Instantiate<ModpackItem>();
			item.ModpackConfig = modpack;
			item.Modulate = item.Modulate with { A = 0 };
			_list!.AddChild(item);
			using var tween = item.CreateTween();
			tween.TweenProperty(item, "modulate:a", 1, 0.4f).SetDelay(i * 0.1);
			i++;
		}
	}
}
