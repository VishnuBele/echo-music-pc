using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace EchoMusicSetup;

internal sealed class SetupWindow : Window
{
    private static readonly Color PanelColor = Color.Parse("#111820");
    private static readonly Color SurfaceColor = Color.Parse("#171F28");
    private static readonly Color AccentColor = Color.Parse("#1ED760");
    private static readonly Color PrimaryText = Color.Parse("#F2F5F7");
    private static readonly Color SecondaryText = Color.Parse("#AAB6C1");

    private readonly TextBox _installPath;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly Button _installButton;
    private readonly CheckBox _launchAfterInstall;
    private readonly string _defaultInstallPath;
    private readonly long _expectedPayloadBytes;
    private bool _installed;
    private string? _failureMessage;

    public SetupWindow()
    {
        Title = "Echo Music Setup";
        Width = 860;
        Height = 570;
        MinWidth = 780;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(PanelColor);
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        CanResize = false;

        _defaultInstallPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "EchoMusic");
        _expectedPayloadBytes = ReadExpectedPayloadBytes();

        var root = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("220,*") };
        root.Children.Add(BuildBrandPanel());

        var main = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            Margin = new Thickness(30, 24, 30, 22),
        };
        Grid.SetColumn(main, 1);
        root.Children.Add(main);

        var header = new StackPanel { Spacing = 6 };
        header.Children.Add(new TextBlock
        {
            Text = "Install Echo Music",
            FontSize = 27,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(PrimaryText),
        });
        header.Children.Add(new TextBlock
        {
            Text = "A native Windows music player, ready in a few steps.",
            FontSize = 13.5,
            Foreground = new SolidColorBrush(SecondaryText),
        });
        header.Children.Add(new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Height = 1,
            Background = new SolidColorBrush(Color.Parse("#2B3742")),
        });
        Grid.SetRow(header, 0);
        main.Children.Add(header);

        var page = new StackPanel { Spacing = 18, Margin = new Thickness(0, 24, 0, 18) };
        page.Children.Add(new TextBlock
        {
            Text = "Your setup",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(PrimaryText),
        });
        page.Children.Add(BuildFeaturePanel());

        var locationLabel = new TextBlock
        {
            Text = "INSTALL LOCATION",
            FontSize = 10.5,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(SecondaryText),
            Margin = new Thickness(0, 2, 0, 7),
        };
        page.Children.Add(locationLabel);
        var locationRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        _installPath = new TextBox
        {
            Text = _defaultInstallPath,
            MinHeight = 42,
            Padding = new Thickness(12, 9),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(SurfaceColor),
            Foreground = new SolidColorBrush(PrimaryText),
            BorderBrush = new SolidColorBrush(Color.Parse("#34424F")),
            CornerRadius = new CornerRadius(9),
        };
        locationRow.Children.Add(_installPath);
        var browse = MakeSecondaryButton("Browse…");
        browse.Margin = new Thickness(10, 0, 0, 0);
        browse.Click += async (_, _) => await BrowseForFolderAsync();
        Grid.SetColumn(browse, 1);
        locationRow.Children.Add(browse);
        page.Children.Add(locationRow);

        _launchAfterInstall = new CheckBox
        {
            Content = "Launch Echo Music when setup finishes",
            IsChecked = true,
            Foreground = new SolidColorBrush(PrimaryText),
            FontSize = 12.5,
        };
        page.Children.Add(_launchAfterInstall);

        var notice = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#182A25")),
            BorderBrush = new SolidColorBrush(Color.Parse("#29513E")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 10),
            Child = new TextBlock
            {
                Text = "This Echo Music setup file is unsigned. Windows may show a reputation warning. Verify its SHA-256 checksum before continuing; if antivirus quarantines it, review the detection and only restore or exclude a verified file you trust.",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.Parse("#D8E7DF")),
                TextWrapping = TextWrapping.Wrap,
            }
        };
        page.Children.Add(notice);

        var scroll = new ScrollViewer
        {
            Content = page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scroll, 1);
        main.Children.Add(scroll);

        var footer = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,Auto"), ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        _status = new TextBlock
        {
            Text = "Installs for your Windows user; no administrator prompt is required.",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(SecondaryText),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        };
        Grid.SetRow(_status, 0);
        Grid.SetColumn(_status, 0);
        footer.Children.Add(_status);
        _installButton = MakePrimaryButton("Install Echo Music");
        _installButton.MinWidth = 176;
        _installButton.Click += async (_, _) =>
        {
            if (_installed)
            {
                await LaunchAndCloseAsync();
                return;
            }
            if (_failureMessage is not null)
            {
                _failureMessage = null;
                await InstallAsync();
                return;
            }
            await InstallAsync();
        };
        Grid.SetRow(_installButton, 0);
        Grid.SetColumn(_installButton, 1);
        footer.Children.Add(_installButton);

        _progress = new ProgressBar
        {
            IsIndeterminate = false,
            IsVisible = false,
            Height = 3,
            Margin = new Thickness(0, 14, 0, 0),
            Background = new SolidColorBrush(Color.Parse("#26323C")),
            Foreground = new SolidColorBrush(AccentColor),
        };
        Grid.SetRow(_progress, 1);
        Grid.SetColumnSpan(_progress, 2);
        footer.Children.Add(_progress);
        main.Children.Add(footer);
        Grid.SetRow(footer, 2);
        Content = root;
    }

    private Control BuildBrandPanel()
    {
        var panel = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#0A2C2B"), 0),
                    new GradientStop(Color.Parse("#0B1922"), 1),
                }
            },
            Padding = new Thickness(26, 30),
        };
        var stack = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Stretch };
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://EchoMusicSetup/Assets/echo-logo.png"));
            stack.Children.Add(new Image
            {
                Source = new Bitmap(stream),
                Width = 102,
                Height = 102,
                Margin = new Thickness(0, 28, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Stretch = Stretch.Uniform,
            });
        }
        catch { }
        stack.Children.Add(new TextBlock
        {
            Text = "ECHO MUSIC",
            FontSize = 21,
            FontWeight = FontWeight.ExtraBold,
            LetterSpacing = 1.2,
            Foreground = new SolidColorBrush(PrimaryText),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "MUSIC, YOUR WAY",
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.1,
            Foreground = new SolidColorBrush(Color.Parse("#8BB5A0")),
        });
        var separator = new Border { Height = 1, Width = 142, Background = new SolidColorBrush(Color.Parse("#38534E")), Margin = new Thickness(0, 24, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(separator);
        var wave = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Height = 42, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        double[] heights = { 20, 34, 16, 28, 40, 24, 15, 32, 20, 37, 23, 14, 31, 20 };
        foreach (var h in heights)
            wave.Children.Add(new Border
            {
                Width = 4,
                Height = h,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(AccentColor),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.92,
            });
        stack.Children.Add(wave);
        stack.Children.Add(new TextBlock
        {
            Text = "NATIVE WINDOWS PLAYER",
            FontSize = 9.5,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 0.6,
            Foreground = new SolidColorBrush(SecondaryText),
            Margin = new Thickness(0, 2, 0, 0),
        });
        panel.Child = stack;
        return panel;
    }

    private Control BuildFeaturePanel()
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(SurfaceColor),
            BorderBrush = new SolidColorBrush(Color.Parse("#2B3742")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12),
        };
        var rows = new StackPanel { Spacing = 8 };
        rows.Children.Add(FeatureLine("●", "Native C# / Avalonia app"));
        rows.Children.Add(FeatureLine("♫", "Streaming, synced lyrics and local music"));
        rows.Children.Add(FeatureLine("▤", "Equalizer, offline mode and Windows media keys"));
        panel.Child = rows;
        return panel;
    }

    private Control FeatureLine(string glyph, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new TextBlock { Text = glyph, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(AccentColor), Width = 18 });
        row.Children.Add(new TextBlock { Text = text, FontSize = 11.5, Foreground = new SolidColorBrush(PrimaryText), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static Button MakePrimaryButton(string text) => new()
    {
        Content = text,
        Background = new SolidColorBrush(AccentColor),
        Foreground = new SolidColorBrush(Color.Parse("#06140C")),
        FontWeight = FontWeight.Bold,
        FontSize = 13,
        Padding = new Thickness(18, 11),
        CornerRadius = new CornerRadius(9),
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };

    private static Button MakeSecondaryButton(string text) => new()
    {
        Content = text,
        Background = new SolidColorBrush(Color.Parse("#202A34")),
        Foreground = new SolidColorBrush(PrimaryText),
        FontSize = 12,
        Padding = new Thickness(14, 9),
        CornerRadius = new CornerRadius(9),
        BorderBrush = new SolidColorBrush(Color.Parse("#34424F")),
        BorderThickness = new Thickness(1),
    };

    private async Task BrowseForFolderAsync()
    {
        try
        {
            var chosen = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose where Echo Music should be installed",
                AllowMultiple = false,
            });
            if (chosen.FirstOrDefault()?.TryGetLocalPath() is { Length: > 0 } path)
            {
                var trimmed = Path.TrimEndingDirectorySeparator(path);
                _installPath.Text = string.Equals(Path.GetFileName(trimmed), "EchoMusic", StringComparison.OrdinalIgnoreCase)
                    ? trimmed
                    : Path.Combine(trimmed, "EchoMusic");
            }
        }
        catch (Exception ex)
        {
            _status.Text = $"Folder picker could not open: {ex.Message}";
        }
    }

    private static long ReadProgressBytes(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text = bytes.Length >= 2 && bytes[1] == 0
            ? Encoding.Unicode.GetString(bytes)
            : Encoding.UTF8.GetString(bytes);
        return long.TryParse(text.Trim('\0', '\r', '\n', ' ', '\t'), out long value) ? value : 0;
    }

    private static long ReadExpectedPayloadBytes()
    {
        try
        {
            var manifest = Path.Combine(AppContext.BaseDirectory, "install-payload-bytes.txt");
            return long.TryParse(File.ReadAllText(manifest).Trim(), out long bytes) && bytes > 0 ? bytes : 1;
        }
        catch { return 1; }
    }

    private async Task InstallAsync()
    {
        var installPath = (_installPath.Text ?? string.Empty).Trim();
        if (installPath.Length == 0)
        {
            _status.Text = "Choose an installation folder first.";
            return;
        }
        if (!Path.IsPathFullyQualified(installPath))
        {
            _status.Text = "Enter a full Windows folder path, such as C:\\Users\\you\\AppData\\Local\\Programs\\EchoMusic.";
            return;
        }

        _installButton.IsEnabled = false;
        _installPath.IsEnabled = false;
        _progress.IsVisible = true;
        _progress.IsIndeterminate = false;
        _progress.Minimum = 0;
        _progress.Maximum = 100;
        _progress.Value = 0;
        _status.Text = "Preparing installation… 0%";

        string progressFile = Path.Combine(installPath, ".echo-install-progress");
        string tempFolder = Path.Combine(Path.GetTempPath(), "EchoMusicSetup-" + Guid.NewGuid().ToString("N"));
        string bundledCoreSetup = Path.Combine(AppContext.BaseDirectory, "EchoMusic-CoreSetup.exe");
        string coreSetup = bundledCoreSetup;
        bool ownsTempFolder = false;
        try
        {
            if (!File.Exists(bundledCoreSetup))
            {
                Directory.CreateDirectory(tempFolder);
                ownsTempFolder = true;
                coreSetup = Path.Combine(tempFolder, "EchoMusicCoreSetup.exe");
                await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("EchoMusicSetup.CoreSetup.exe");
                if (resource is null)
                    throw new InvalidOperationException("The installation package is missing. Please download the complete installer again.");
                await using var output = new FileStream(coreSetup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await resource.CopyToAsync(output);
            }

            var start = new ProcessStartInfo(coreSetup)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(coreSetup) ?? AppContext.BaseDirectory,
            };
            start.ArgumentList.Add("/S");
            start.ArgumentList.Add("/D=" + installPath);
            try { if (File.Exists(progressFile)) File.Delete(progressFile); } catch { }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows could not start the installer engine.");
            while (!process.HasExited)
            {
                try
                {
                    long writtenBytes = ReadProgressBytes(progressFile);
                    if (writtenBytes > 0)
                    {
                        int percent = (int)Math.Clamp(Math.Floor(writtenBytes * 95.0 / _expectedPayloadBytes), 0, 95);
                        _progress.Value = percent;
                        _status.Text = $"Installing Echo Music… {percent}%";
                    }
                }
                catch { /* The progress file may briefly be absent while the installer starts or updates it. */ }
                await Task.Delay(250);
            }
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"The installer engine exited with code {process.ExitCode}. Check available disk space and permissions, then retry.");

            _progress.Value = 100;
            _installed = true;
            _status.Text = $"Installed to {installPath}. A Start Menu shortcut has been added. 100%";
            _installButton.Content = "Finish";
            _installButton.IsEnabled = true;
            _progress.IsVisible = false;
        }
        catch (Exception ex)
        {
            _failureMessage = ex.Message;
            _status.Text = "Installation could not be completed. " + ex.Message;
            _installButton.Content = "Retry installation";
            _installButton.IsEnabled = true;
            _progress.IsVisible = false;
            _installPath.IsEnabled = true;
        }
        finally
        {
            try { if (File.Exists(progressFile)) File.Delete(progressFile); } catch { }
            if (ownsTempFolder)
                try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, recursive: true); } catch { }
        }
    }

    private async Task LaunchAndCloseAsync()
    {
        if (_launchAfterInstall.IsChecked == true)
        {
            string appPath = Path.Combine((_installPath.Text ?? _defaultInstallPath).Trim(), "EchoMusic.exe");
            try { Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = true }); }
            catch (Exception ex) { _status.Text = $"Installed, but Echo Music could not be launched: {ex.Message}"; return; }
        }
        await Task.CompletedTask;
        Close();
    }
}
