using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MVL.Utils.Config;
using MVL.Utils.Extensions;
using MVL.Utils.Game;
using MVL.Utils.Help;

namespace MVL.UI.Window;

public partial class AddModpackWindow : BaseWindow {

	[Export]
	private LineEdit? _modpackName;

	[Export]
	private LineEdit? _modpackPath;

	[Export]
	private CheckButton? _createPath;

	[Export]
	private Button? _folderButton;

	[Export]
	private FileDialog? _fileDialog;

	[Export]
	private OptionButton? _gameVersion;

	[Export]
	private OptionButton? _releasePath;

	[Export]
	private Label? _tooltip;

	[Signal]
	public delegate void AddModpackEventHandler(string modpackName, string modpackPath, string gameVersion, string releasePath);

	private Dictionary<string, List<string>> _dictionary = [];
	private int _sourceRevision;
	public bool ImportMode { get; set; }
	public ModpackManifest? ImportManifest { get; private set; }

	public override void _Ready() {
		base._Ready();
		NullExceptionHelper.NotNull(
			_modpackName,
			_modpackPath,
			_createPath,
			_folderButton,
			_fileDialog,
			_gameVersion,
			_releasePath,
			_tooltip,
			CancelButton,
			OkButton);
		Main.CheckReleaseInfo();

		_gameVersion.GetPopup().MaxSize = _gameVersion.GetPopup().MaxSize with { Y = 130 };
		_releasePath.GetPopup().MaxSize = _releasePath.GetPopup().MaxSize with { Y = 130 };
		_gameVersion.ItemSelected += GameVersionOnItemSelected;

		var list = Main.ReleaseInfos.Values.OrderByDescending(info => info.Version, GameVersion.Comparer);
		_dictionary = [];
		foreach (var releaseInfo in list) {
			var version = releaseInfo.Version.ShortGameVersion;
			var path = releaseInfo.Path;
			if (!_dictionary.TryGetValue(version, out var paths)) {
				paths = [];
				_dictionary[version] = paths;
			}

			paths.Add(path);
		}

		foreach (var version in _dictionary.Keys) {
			_gameVersion!.AddItem(version);
		}

		var name = _modpackName.Text;
		_modpackName.TextChanged += text => {
			var sanitizedText = FolderName(text);
			var path = _modpackPath!.Text.NormalizePath();

			if (_createPath!.ButtonPressed && !string.IsNullOrEmpty(path)) {
				var currentDirectoryName = Path.GetFileName(path);
				var shouldModifyPath = string.IsNullOrEmpty(name) ||
					string.Equals(currentDirectoryName, name);

				if (shouldModifyPath) {
					var baseDirectory = string.IsNullOrEmpty(name)
						? path
						: path.GetBaseDir();

					path = Path.Combine(baseDirectory, sanitizedText).NormalizePath();
				}

				SetModpackPath(path);
			}

			name = sanitizedText;

			ValidateInputs();
		};

		_modpackPath.TextChanged += ModpackPathOnTextChanged;
		_createPath.Toggled += CreatePathOnToggled;
		_folderButton.Pressed += _fileDialog.Show;
		_fileDialog.CurrentPath = Main.BaseConfig.ModpackFolder;
		_fileDialog.CurrentDir = Main.BaseConfig.ModpackFolder;
		_fileDialog.DirSelected += FileDialogOnDirSelected;
		CancelButton.Pressed += CancelButtonOnPressed;
		OkButton.Pressed += OkButtonOnPressed;

		if (_gameVersion.ItemCount > 0) {
			GameVersionOnItemSelected(0);
		} else {
			_gameVersion.Select(-1);
		}

		SetModpackPath(Path.Combine(Main.BaseConfig.ModpackFolder, _modpackName.Text).NormalizePath());
		if (ImportMode) {
			TitleLabel!.Text = "导入整合包";
			OkButton.Text = "导入";
			GetNode<Control>("%LinkRow").Show();
			var link = GetNode<LineEdit>("%ShareLink");
			link.TextChanged += text => _ = LoadImportSourceAsync(text);
			GetNode<Button>("%ChooseManifestButton").Pressed += ChooseManifestButtonOnPressed;
		}
	}

	private async Task LoadImportSourceAsync(string text) {
		var revision = ++_sourceRevision;
		var status = GetNode<Label>("%LinkStatus");
		ImportManifest = null;
		if (string.IsNullOrWhiteSpace(text)) {
			status.Hide();
			ValidateInputs();
			return;
		}
		status.Text = "正在读取整合包清单…";
		status.Modulate = Colors.Yellow;
		status.Show();
		await Task.Delay(350);
		if (revision != _sourceRevision || !IsInstanceValid(this)) return;
		var (loadedManifest, message) = await ModpackManifest.ReadSourceAsync(text);
		if (revision != _sourceRevision || !IsInstanceValid(this)) return;
		ImportManifest = loadedManifest;
		if (ImportManifest is { } manifest) {
			var index = _dictionary.Keys.ToList().IndexOf(manifest.GameVersion?.ShortGameVersion ?? "");
			_gameVersion!.Select(index);
			if (index >= 0) {
				GameVersionOnItemSelected(index);
			} else {
				_releasePath!.Clear();
				message = Tr("请选择本机已安装的游戏版本");
			}
		}
		status.Text = Tr(message);
		status.Visible = ImportManifest is null || message.Length > 0;
		status.Modulate = ImportManifest is null ? Colors.Red : Colors.Yellow;
		ValidateInputs();
	}

	private void ChooseManifestButtonOnPressed() {
		var dialog = new FileDialog {
			Access = FileDialog.AccessEnum.Filesystem,
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Filters = ["*.json", "JSON files"],
			UseNativeDialog = true
		};
		dialog.Canceled += dialog.QueueFree;
		dialog.FileSelected += path => {
			dialog.QueueFree();
			GetNode<LineEdit>("%ShareLink").Text = path;
			_ = LoadImportSourceAsync(path);
		};
		AddChild(dialog);
		dialog.Show();
	}

	private void FileDialogOnDirSelected(string path) {
		if (_createPath!.ButtonPressed) {
			path = Path.Combine(path, FolderName(_modpackName!.Text));
		}

		SetModpackPath(path.NormalizePath());
	}

	private void GameVersionOnItemSelected(long index) {
		_releasePath!.Clear();
		var s = _gameVersion!.GetItemText((int)index);
		if (_dictionary.TryGetValue(s, out var paths)) {
			foreach (var path in paths) {
				_releasePath.AddItem(path);
			}
		}

		if (_releasePath.ItemCount > 0) {
			_releasePath.Select(0);
		} else {
			_releasePath.Select(-1);
		}
		if (ImportMode && ImportManifest != null) {
			GetNode<Label>("%LinkStatus").Hide();
			ValidateInputs();
		}
	}

	private void SetModpackPath(string path) {
		_modpackPath!.Text = path;
		ValidateInputs();
	}

	private void CreatePathOnToggled(bool toggledOn) {
		var name = FolderName(_modpackName!.Text);
		var path = _modpackPath!.Text;
		if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) {
			return;
		}

		path = toggledOn switch {
			true when path.GetFile() != name => Path.Combine(path, name),
			false when path.GetFile() == name => path.GetBaseDir(),
			_ => path
		};

		SetModpackPath(path);
	}

	private void ModpackPathOnTextChanged(string text) { ValidateInputs(); }

	private static string FolderName(string text) {
		var name = Path.GetInvalidFileNameChars().Aggregate(text, (value, ch) => value.Replace(ch, '_')).TrimEnd(' ', '.');
		return name.Length == 0 && text.Length > 0 ? "_" : name;
	}

	private void ValidateInputs() {
		var name = _modpackName!.Text;
		var path = _modpackPath!.Text.NormalizePath();

		if (_gameVersion!.ItemCount == 0) {
			_gameVersion.Disabled = true;
			_releasePath!.Disabled = true;
			OkButton!.Disabled = true;
			_tooltip!.Text = "请先在版本界面中导入游戏";
			_tooltip.Modulate = Colors.Red;
			return;
		}

		if (string.IsNullOrEmpty(name)) {
			OkButton!.Disabled = true;
			_tooltip!.Text = "请输入名称";
			_tooltip.Modulate = Colors.Red;
			return;
		}

		if (string.IsNullOrEmpty(path)) {
			OkButton!.Disabled = true;
			_tooltip!.Text = "请输入路径";
			_tooltip.Modulate = Colors.Red;
			return;
		}

		if (path.IndexOfAny(Path.GetInvalidPathChars()) != -1) {
			OkButton!.Disabled = true;
			_tooltip!.Text = "路径包含非法字符";
			_tooltip.Modulate = Colors.Red;
			return;
		}

		if (ImportMode) {
			OkButton!.Disabled = true;
			if (ImportManifest == null || _gameVersion.Selected < 0) return;
			if (File.Exists(Path.Combine(path, "modpack.json")) || Main.ModpackConfigs.ContainsKey(path)) {
				_tooltip!.Text = "此目录已有整合包，请选择其他目录";
				_tooltip.Modulate = Colors.Red;
				return;
			}
		}

		var createDir = _createPath!.ButtonPressed;
		var dirPath = createDir ? path.GetBaseDir() : path;

		if (!Directory.Exists(dirPath)) {
			OkButton!.Disabled = true;
			_tooltip!.Text = createDir ? "父文件夹不存在" : "文件夹不存在";
			_tooltip.Modulate = Colors.Red;
			return;
		}

		OkButton!.Disabled = false;
		_tooltip!.Text = "将自动创建文件夹";
		_tooltip.Modulate = Colors.White;

		if (!Directory.Exists(path)) {
			return;
		}

		_tooltip!.Text = Directory.GetFileSystemEntries(path).Length > 0 ? "文件夹不为空，确定选择它吗？" : "文件夹存在且为空";
		_tooltip.Modulate = Directory.GetFileSystemEntries(path).Length > 0 ? Colors.Yellow : Colors.White;
	}

	private async void OkButtonOnPressed() {
		var name = _modpackName!.Text;
		var path = _modpackPath!.Text.NormalizePath();

		if (_createPath!.ButtonPressed) {
			if (!Directory.Exists(path) && DirAccess.MakeDirAbsolute(path) != Error.Ok) {
				_tooltip!.Text = "无法创建文件夹";
				_tooltip.Modulate = Colors.Red;
				return;
			}
		}

		OkButton!.Disabled = true;
		await Hide();
		EmitSignalAddModpack(name,
			Path.TrimEndingDirectorySeparator(path),
			_gameVersion!.GetItemText(_gameVersion.Selected),
			_releasePath!.GetItemText(_releasePath.Selected));
	}
}
