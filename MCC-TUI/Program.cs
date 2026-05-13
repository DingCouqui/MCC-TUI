using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Terminal.Gui;

namespace MccTui;

class Program
{
    private static string BaseDir => AppContext.BaseDirectory;
    private static string ConfigDir => Path.Combine(BaseDir, "config");

    private static string McClientExe =>
        OperatingSystem.IsWindows()
            ? Path.Combine(BaseDir, "MinecraftClient.exe")
            : Path.Combine(BaseDir, "MinecraftClient");

    private static ListView? _fileListView;
    private static FrameView? _fileListFrame;

    static void Main(string[] args)
    {
        LocalizationManager.Initialize();

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        Application.Init(driverName: "NetDriver");

        var window = new Window
        {
            Title = L("title"),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        window.KeyDown += (s, e) =>
        {
            if (e == Key.Esc)
                e.Handled = true;
        };

        var manageLabel = new Label
        {
            Text = L("manage_label"),
            X = 0,
            Y = 0,
            Width = Dim.Auto(),
            Height = 1,
            CanFocus = true
        };

        var addButton = new Button
        {
            Title = L("add_button"),
            X = Pos.Right(manageLabel),
            Y = 0,
            Width = Dim.Auto(),
            Height = 1
        };
        addButton.Accept += OnAddButtonClicked;

        manageLabel.KeyDown += (s, e) =>
        {
            if (e == Key.CursorRight)
            {
                addButton.SetFocus();
                e.Handled = true;
            }
        };

        addButton.KeyDown += (s, e) =>
        {
            if (e == Key.CursorLeft)
            {
                manageLabel.SetFocus();
                e.Handled = true;
            }
        };

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
                addButton.SetFocus();
                e.Handled = true;
            }
        };
        _fileListFrame.Add(_fileListView);

        window.Add(manageLabel);
        window.Add(addButton);
        window.Add(_fileListFrame);

        Application.Run(window);
        Application.Shutdown();
    }

    private static void OnAddButtonClicked(object? sender, HandledEventArgs e)
    {
        if (!Directory.Exists(ConfigDir))
        {
            MessageBox.ErrorQuery(L("error"), $"{L("error_config_not_found")}{Environment.NewLine}{ConfigDir}", L("ok"));
            return;
        }

        var iniFiles = Directory.GetFiles(ConfigDir, "*.ini")
            .Select(f => Path.GetFileName(f)!)
            .ToList();

        if (iniFiles.Count == 0)
        {
            MessageBox.ErrorQuery(L("hint"), L("error_no_ini_files"), L("ok"));
            return;
        }

        _fileListView!.SetSource(new ObservableCollection<string>(iniFiles));
        _fileListFrame!.Visible = true;
        _fileListView.SetFocus();
    }

    private static void OnFileSelected(object? sender, ListViewItemEventArgs e)
    {
        if (e.Value is string selectedFile)
        {
            var configArg = $"config/{selectedFile}";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = McClientExe,
                    Arguments = $"\"{configArg}\"",
                    UseShellExecute = true,
                    WorkingDirectory = BaseDir
                });
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery(L("error"), $"{L("error_launch_failed")}{Environment.NewLine}{ex.Message}", L("ok"));
            }
        }
    }

    private static string L(string key) => LocalizationManager.Get(key);
}
