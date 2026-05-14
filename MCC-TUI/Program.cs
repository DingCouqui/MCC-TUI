using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Terminal.Gui;

namespace MccTui;

sealed class InstanceInfo
{
    public string Name = "";
    public Label TabLabel = null!;
    public Process? Process;
    public ConcurrentQueue<string> OutputQueue = new();
    public ObservableCollection<string> OutputLines = new();
    public bool HasExited;
    public int ExitCode;
}

partial class Program
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

    private static FrameView? _instanceOutputFrame;
    private static ColoredOutputView? _outputView;
    private static InstanceInfo? _activeOutputInstance;
    private static bool _outputAutoScroll = true;
    private static int _pollCounter;
    private static DateTime _lastPollTime = DateTime.MinValue;

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
            DebugLogger.Log($"WinKeyDown: {e.KeyCode}, handled={e.Handled}");
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
            DebugLogger.Log($"FileListKey: {e.KeyCode}");
            if (e == Key.Esc || e == Key.Backspace)
            {
                _fileListFrame!.Visible = false;
                _addButton!.SetFocus();
                e.Handled = true;
            }
        };
        _fileListFrame.Add(_fileListView);

        _instanceOutputFrame = new FrameView
        {
            Title = L("output_title"),
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 1,
            Visible = false
        };

        var outputColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.White, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.White, Color.Black),
        };

        _outputView = new ColoredOutputView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            ColorScheme = outputColorScheme
        };
        _outputView.KeyDown += (s, e) =>
        {
            DebugLogger.Log($"OutputViewKey: {e.KeyCode}, vis={_instanceOutputFrame!.Visible}");
            if (!_instanceOutputFrame.Visible)
                return;
            if (e == Key.Esc || e == Key.Backspace)
            {
                ExitInstanceView();
                e.Handled = true;
            }
        };
        _instanceOutputFrame.Add(_outputView);
        _instanceOutputFrame.ColorScheme = outputColorScheme;

        _window.Add(_manageLabel);
        _window.Add(_addButton);
        _window.Add(_fileListFrame);
        _window.Add(_instanceOutputFrame);

        RebuildNavigation();

        var pollTimer = new System.Threading.Timer(_ =>
        {
            Application.Invoke(() => PollOutputOnIdle());
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(200));

        DebugLogger.Log("App: entering Run");
        Application.Run(_window);
        DebugLogger.Log("App: exited Run");
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
                var name = Path.GetFileNameWithoutExtension(selectedFile);
                var info = AddInstanceTab(name);

                DebugLogger.Log($"Launching MCC: {McClientExe} \"{configArg}\" BasicIO");

                var psi = new ProcessStartInfo
                {
                    FileName = McClientExe,
                    Arguments = $"\"{configArg}\" BasicIO",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    WorkingDirectory = BaseDir
                };

                var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                process.OutputDataReceived += (s, ev) =>
                {
                    if (ev.Data != null)
                        info.OutputQueue.Enqueue(SanitizeOutput(ev.Data));
                };

                process.ErrorDataReceived += (s, ev) =>
                {
                    if (ev.Data != null)
                        info.OutputQueue.Enqueue(SanitizeOutput(ev.Data));
                };

                process.Exited += (s, ev) =>
                {
                    info.HasExited = true;
                    info.ExitCode = process.ExitCode;
                    info.OutputQueue.Enqueue($"--- {L("process_exited")} ({info.ExitCode}) ---");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                info.Process = process;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Launch failed: {ex.Message}");
                MessageBox.ErrorQuery(L("error"), $"{L("error_launch_failed")}{Environment.NewLine}{ex.Message}", L("ok"));
            }
        }
    }

    private static InstanceInfo AddInstanceTab(string name)
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

        label.KeyDown += (s, ev) =>
        {
            DebugLogger.Log($"TabEnter[{name}]: {ev.KeyCode}");
            if (ev == Key.Enter)
            {
                EnterInstanceView(info);
                ev.Handled = true;
            }
        };

        Application.Refresh();
        return info;
    }

    private static void RebuildNavigation()
    {
        DebugLogger.Log($"RebuildNav: {_instanceInfos.Count} instances");

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
                DebugLogger.Log($"ManageKey: {ev.KeyCode}");
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
                DebugLogger.Log($"ManageKey: {ev.KeyCode}");
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
                DebugLogger.Log($"AddBtnKey: {ev.KeyCode}");
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
                DebugLogger.Log($"AddBtnKey: {ev.KeyCode}");
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
                DebugLogger.Log($"TabKey[{index}]: {ev.KeyCode}");
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

    private static void EnterInstanceView(InstanceInfo info)
    {
        DebugLogger.Log($"Entering instance view: [{info.Name}]");

        _activeOutputInstance = info;
        _instanceOutputFrame!.Title = string.Format(L("output_title"), info.Name);
        _outputView!.Lines = info.OutputLines;
        _instanceOutputFrame.Visible = true;
        _outputView.SetFocus();
    }

    private static void ExitInstanceView()
    {
        DebugLogger.Log("Exiting instance view");

        _instanceOutputFrame!.Visible = false;

        var returning = _activeOutputInstance;
        _activeOutputInstance = null;

        if (returning != null)
            returning.TabLabel.SetFocus();

        _outputView!.Lines = null;
    }

    private static bool PollOutputOnIdle()
    {
        if ((DateTime.UtcNow - _lastPollTime).TotalMilliseconds < 50)
            return true;

        _lastPollTime = DateTime.UtcNow;
        return PollOutputQueues();
    }

    private static bool PollOutputQueues()
    {
        _pollCounter++;

        if (_activeOutputInstance == null)
        {
            if (_pollCounter % 120 == 0)
                DebugLogger.Log($"PollOutput: #{_pollCounter} (no active)");
            return true;
        }

        if (_pollCounter % 20 == 0)
            DebugLogger.Log($"PollOutput: #{_pollCounter} active=[{_activeOutputInstance.Name}]");

        var info = _activeOutputInstance;
        var lines = info.OutputLines;
        var queue = info.OutputQueue;
        bool added = false;

        while (queue.TryDequeue(out var line))
        {
            lines.Add(line);
            added = true;
        }

        while (lines.Count > 10000)
            lines.RemoveAt(0);

        if (added)
        {
            var wasAtEnd = _outputAutoScroll || _outputView!.IsAtEnd;

            _outputView!.Lines = info.OutputLines;

            if (wasAtEnd)
                _outputView!.MoveEnd();
        }

        return true;
    }

    private static void SendInputToInstance(InstanceInfo info, string text)
    {
        if (info.Process is { HasExited: false } p)
        {
            DebugLogger.Log($"Sending input to [{info.Name}]: {text}");
            p.StandardInput.WriteLine(text);
        }
    }

    private static string SanitizeOutput(string line)
    {
        line = AnsiEscapeRegex().Replace(line, "");
        return line.Replace("\r", "");
    }

    [GeneratedRegex(@"\x1b\[[0-9;]*m")]
    private static partial Regex AnsiEscapeRegex();

    private static string L(string key) => LocalizationManager.Get(key);
}
