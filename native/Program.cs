using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MusicWidget.NativeApp
{
    internal static class Program
    {
        [STAThread]
        internal static int Main(string[] args)
        {
            try
            {
                var options = Options.Parse(args);
                if (options.InstallTools) return InstallOnlineTools(options);
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                bool primary;
                string suffix = options.Isolated ? "." + options.DataDirectory.ToUpperInvariant().GetHashCode().ToString("x") : "";
                using (var mutex = new Mutex(true, "Local\\MusicWidget.Player" + suffix, out primary))
                {
                    using (var reveal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\MusicWidget.Show" + suffix))
                    {
                        if (!primary) { reveal.Set(); return 0; }
                        using (var model = new PlayerModel(options.DataDirectory, options.MusicDirectory, Path.Combine(options.DataDirectory, "bin")))
                        {
                            var window = new WidgetWindow(model);
                            if (!options.NoTray)
                                AfterFirstFrame(window, () => window.InitializeWindowsIntegration(options.Portable, reveal));
                            if (options.ReadyFile != null)
                            {
                                bool reported = false;
                                window.ContentRendered += (sender, e) =>
                                {
                                    if (reported) return;
                                    reported = true;
                                    NativeBackdrop.FlushComposition();
                                    WriteReady(options.ReadyFile, window);
                                };
                            }
                            if (options.Pane != null) window.Navigate(options.Pane);
                            return app.Run(window);
                        }
                    }
                }
            }
            catch (Exception failure)
            {
                // Keep GUI launches diagnosable after a runtime or filesystem error.
                if (Array.IndexOf(args, "--no-tray") >= 0 || Array.IndexOf(args, "--enable-online-tools-quiet") >= 0)
                {
                    Console.Error.WriteLine(failure);
                    string errorFile = ArgumentValue(args, "--ready-file");
                    if (errorFile != null)
                    {
                        try { File.WriteAllText(errorFile + ".error.txt", failure.ToString()); } catch { }
                    }
                }
                else MessageBox.Show(failure.Message, "Music Widget", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        private static string ArgumentValue(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        internal static void AfterFirstFrame(WidgetWindow window, Action callback)
        {
            EventHandler firstFrame = null;
            firstFrame = (sender, args) =>
            {
                window.ContentRendered -= firstFrame;
                window.Dispatcher.BeginInvoke(DispatcherPriority.Background, callback);
            };
            window.ContentRendered += firstFrame;
        }

        private static void WriteReady(string path, WidgetWindow widget)
        {
            var snapshot = new
            {
                utc = DateTime.UtcNow.ToString("o"),
                pid = Process.GetCurrentProcess().Id,
                width = widget.ActualWidth,
                height = widget.ActualHeight,
                hwnd = widget.Hwnd.ToInt64(),
                acrylic = widget.AcrylicActive
            };
            string temporary = path + "." + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(snapshot), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private static int InstallOnlineTools(Options options)
        {
            var directory = Path.Combine(options.DataDirectory, "bin");
            if (options.Quiet)
            {
                OnlineToolInstaller.InstallAsync(directory, null, CancellationToken.None).GetAwaiter().GetResult();
                return 0;
            }

            bool french = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
            var app = new Application();
            var window = new Window
            {
                Title = "Music Widget", Width = 425, Height = 175, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(25, 28, 40))
            };
            var stack = new StackPanel { Margin = new Thickness(23) };
            var label = new TextBlock
            {
                Text = french ? "Installation des outils en ligne" : "Installing online tools",
                FontWeight = FontWeights.SemiBold, FontSize = 17, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(label);
            var gauge = new ProgressBar { Height = 5, IsIndeterminate = true, Margin = new Thickness(0, 16, 0, 12) };
            stack.Children.Add(gauge);
            var details = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
            stack.Children.Add(details);
            window.Content = stack;
            int result = 1;
            using (var cancel = new CancellationTokenSource())
            {
                window.Closed += (sender, args) => cancel.Cancel();
                window.Loaded += async (sender, args) =>
                {
                    try
                    {
                        var progress = new Progress<OnlineInstallProgress>(value =>
                        {
                            gauge.IsIndeterminate = value.TotalBytes <= 0 || value.Verifying;
                            if (!gauge.IsIndeterminate) gauge.Value = Math.Min(100, value.BytesReceived * 100.0 / value.TotalBytes);
                            details.Text = value.Tool;
                        });
                        await OnlineToolInstaller.InstallAsync(directory, progress, cancel.Token);
                        result = 0;
                        label.Text = french ? "Les outils en ligne sont prêts." : "Online tools are ready.";
                        gauge.Value = 100;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception error)
                    {
                        label.Text = french ? "L’installation a échoué" : "Installation failed";
                        details.Text = error.Message;
                    }
                    gauge.IsIndeterminate = false;
                };
                app.Run(window);
            }
            return result;
        }

        private sealed class Options
        {
            internal string DataDirectory, MusicDirectory, ReadyFile, Pane;
            internal bool Portable, Isolated, NoTray, InstallTools, Quiet;

            internal static Options Parse(string[] args)
            {
                var options = new Options { Portable = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "portable.txt")) };
                for (int i = 0; i < args.Length; ++i)
                {
                    switch (args[i])
                    {
                        case "--portable": options.Portable = true; break;
                        case "--no-tray": options.NoTray = true; break;
                        case "--data-dir": options.DataDirectory = Path.GetFullPath(args[++i]); options.Isolated = true; break;
                        case "--ready-file": options.ReadyFile = Path.GetFullPath(args[++i]); break;
                        case "--pane": options.Pane = args[++i]; break;
                        case "--enable-online-tools": options.InstallTools = true; break;
                        case "--enable-online-tools-quiet": options.InstallTools = options.Quiet = true; break;
                        default: throw new ArgumentException("Unknown option: " + args[i]);
                    }
                }
                if (options.NoTray && !options.Isolated)
                    throw new ArgumentException("--no-tray requires an isolated --data-dir.");
                if (options.DataDirectory == null)
                    options.DataDirectory = options.Portable ? AppDomain.CurrentDomain.BaseDirectory :
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicWidget", "Data");
                options.MusicDirectory = Path.Combine(options.DataDirectory, "musique");
                if (options.Portable && !options.Isolated && !Directory.Exists(options.MusicDirectory))
                {
                    var parent = Directory.GetParent(Path.GetFullPath(options.DataDirectory).TrimEnd(Path.DirectorySeparatorChar));
                    if (parent != null)
                    {
                        var sibling = Path.Combine(parent.FullName, "musique");
                        if (Directory.Exists(sibling)) options.MusicDirectory = sibling;
                    }
                }
                return options;
            }
        }
    }
}
