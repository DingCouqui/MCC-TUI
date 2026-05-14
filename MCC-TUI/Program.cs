using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Terminal.Gui;

namespace MccTui;

sealed class InstanceInfo
{
    public string Name = "";
    public Label TabLabel = null!;
}

class Program
{
    private static string BaseDir => AppContext.BaseDirectory;
    private static string ConfigDir => Path.Combine(BaseDir, "config");

    private static string McClientExe =>
        OperatingSystem.IsWindows()
            ? Path.Combine(BaseDir, "MinecraftClient.exe")
            : Path.Combine(BaseDir, "MinecraftClient");

    private static Window? _window;
    private static Label? _manageLabel;
    private static Button? _addButton;
    private static ListView? _fileListView;
    private static FrameView? _fileListFrame;

    private static readonly List<InstanceInfo> _instanceInfos = new();

    private static EventHandler<Key>? _manageKeyHandler;
    private static EventHandler<Key>? _addBtnKeyHandler;
    private static readonly List<EventHandler<Key>> _instanceKeyHandlers = new();

    static void Main(string[] args)
    {
        LocalizationManager.Initialize();
        DebugLogger.Initialize(BaseDir);

        DebugLogger.Log($"MCC-TUI starting, baseDir={BaseDir}, debug={LocalizationManager.IsDebugEnabled}, lang={LocalizationManager.CurrentLanguage}");

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        Application.Init(driverName: "NetDriver");

        _window = new Window
        {
            Title = L("title"),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _window.KeyDown += (s, e) =>
        {
            if (e == Key.Esc)
                e.Handled = true;
        };

        _manageLabel = new Label
        {
            Text = L("manage_label"),
            X = 0,
            Y = 0,
            Width = Dim.Auto(),
            Height = 1,
            CanFocus = true
        };

        _addButton = new Button
        {
            Title = L("add_button"),
            X = Pos.Right(_manageLabel),
            Y = 0,
            Width = Dim.Auto(),
            Height = 1
        };
        _addButton.Accept += OnAddButtonClicked;

        _fileListFrame = new FrameView
        {
            Title = L("file_list_title"),
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 1,
            Visible = false
        };

        _fileListView = new ListView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _fileListView.OpenSelectedItem += OnFileSelected;
        _fileListView.KeyDown += (s, e) =>
        {
            if (e == Key.Esc || e == Key.Backspace)
            {
                _fileListFrame!.Visible = false;
                _addButton!.SetFocus();
                e.Handled = true;
            }
        };
        _fileListFrame.Add(_fileListView);

        _window.Add(_manageLabel);
        _window.Add(_addButton);
        _window.Add(_fileListFrame);

        RebuildNavigation();

        Application.Run(_window);
        Application.Shutdown();
    }

    private static void OnAddButtonClicked(object? sender, HandledEventArgs e)
    {
        DebugLogger.Log($"[+] clicked");

        if (!Directory.Exists(ConfigDir))
        {
            DebugLogger.Log($"Config directory not found: {ConfigDir}");
            MessageBox.ErrorQuery(L("error"), $"{L("error_config_not_found")}{Environment.NewLine}{ConfigDir}", L("ok"));
            return;
        }

        var iniFiles = Directory.GetFiles(ConfigDir, "*.ini")
            .Select(f => Path.GetFileName(f)!)
            .ToList();

        if (iniFiles.Count == 0)
        {
            DebugLogger.Log("No .ini files found in config directory");
            MessageBox.ErrorQuery(L("hint"), L("error_no_ini_files"), L("ok"));
            return;
        }

        DebugLogger.Log($"Loaded {iniFiles.Count} .ini file(s)");

        _fileListView!.SetSource(new ObservableCollection<string>(iniFiles));
        _fileListFrame!.Visible = true;
        _fileListView.SetFocus();
    }

    private static void OnFileSelected(object? sender, ListViewItemEventArgs e)
    {
        if (e.Value is string selectedFile)
        {
            var configArg = $"config/{selectedFile}";

            DebugLogger.Log($"Selected file: {selectedFile}, arg: {configArg}");

            try
            {
                DebugLogger.Log($"Launching MCC: {McClientExe} \"{configArg}\"");
                Process.Start(new ProcessStartInfo
                {
                    FileName = McClientExe,
                    Arguments = $"\"{configArg}\"",
                    UseShellExecute = true,
                    WorkingDirectory = BaseDir
                });
                AddInstanceTab(Path.GetFileNameWithoutExtension(selectedFile));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Launch failed: {ex.Message}");
                MessageBox.ErrorQuery(L("error"), $"{L("error_launch_failed")}{Environment.NewLine}{ex.Message}", L("ok"));
            }
        }
    }

    private static void AddInstanceTab(string name)
    {
        DebugLogger.Log($"Adding instance tab: [{name}] ({_instanceInfos.Count + 1} total)");

        var labelText = $"[{name}]";
        var label = new Label
        {
            Text = labelText,
            Y = 0,
            Width = Dim.Auto(),
            Height = 1,
            CanFocus = true
        };

        if (_instanceInfos.Count == 0)
            label.X = Pos.Right(_manageLabel!);
        else
            label.X = Pos.Right(_instanceInfos[^1].TabLabel);

        var info = new InstanceInfo { Name = name, TabLabel = label };
        _instanceInfos.Add(info);
        _window!.Add(label);

        _addButton!.X = Pos.Right(label);

        RebuildNavigation();
        Application.Refresh();
    }

    private static void RebuildNavigation()
    {
        if (_manageKeyHandler != null)
            _manageLabel!.KeyDown -= _manageKeyHandler;
        if (_addBtnKeyHandler != null)
            _addButton!.KeyDown -= _addBtnKeyHandler;

        for (int i = 0; i < _instanceInfos.Count; i++)
        {
            if (i < _instanceKeyHandlers.Count && _instanceKeyHandlers[i] != null)
                _instanceInfos[i].TabLabel.KeyDown -= _instanceKeyHandlers[i];
        }

        _manageKeyHandler = null;
        _addBtnKeyHandler = null;
        _instanceKeyHandlers.Clear();

        if (_instanceInfos.Count > 0)
        {
            _manageKeyHandler = (s, ev) =>
            {
                if (ev == Key.CursorRight)
                {
                    _instanceInfos[0].TabLabel.SetFocus();
                    ev.Handled = true;
                }
            };
        }
        else
        {
            _manageKeyHandler = (s, ev) =>
            {
                if (ev == Key.CursorRight)
                {
                    _addButton!.SetFocus();
                    ev.Handled = true;
                }
            };
        }
        _manageLabel!.KeyDown += _manageKeyHandler;

        if (_instanceInfos.Count > 0)
        {
            _addBtnKeyHandler = (s, ev) =>
            {
                if (ev == Key.CursorLeft)
                {
                    _instanceInfos[^1].TabLabel.SetFocus();
                    ev.Handled = true;
                }
            };
        }
        else
        {
            _addBtnKeyHandler = (s, ev) =>
            {
                if (ev == Key.CursorLeft)
                {
                    _manageLabel!.SetFocus();
                    ev.Handled = true;
                }
            };
        }
        _addButton!.KeyDown += _addBtnKeyHandler;

        for (int i = 0; i < _instanceInfos.Count; i++)
        {
            int index = i;
            EventHandler<Key> handler = (s, ev) =>
            {
                if (ev == Key.CursorLeft)
                {
                    if (index > 0)
                        _instanceInfos[index - 1].TabLabel.SetFocus();
                    else
                        _manageLabel!.SetFocus();
                    ev.Handled = true;
                }
                else if (ev == Key.CursorRight)
                {
                    if (index < _instanceInfos.Count - 1)
                        _instanceInfos[index + 1].TabLabel.SetFocus();
                    else
                        _addButton!.SetFocus();
                    ev.Handled = true;
                }
            };
            _instanceKeyHandlers.Add(handler);
            _instanceInfos[i].TabLabel.KeyDown += handler;
        }
    }

    private static string L(string key) => LocalizationManager.Get(key);
}
