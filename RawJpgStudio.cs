using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace RawJpgStudio
{
    public sealed class App : Application
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var app = new App();
            if (args.Length == 2 && args[0] == "--smoke-test")
            {
                try { var window = new MainWindow(); File.WriteAllText(args[1], "OK", Encoding.UTF8); window.Close(); }
                catch (Exception ex) { File.WriteAllText(args[1], ex.ToString(), Encoding.UTF8); Environment.ExitCode = 1; }
                return;
            }
            if (args.Length == 2 && args[0] == "--render-preview")
            {
                var window = new MainWindow();
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(760, 670));
                content.Arrange(new Rect(0, 0, 760, 670));
                content.UpdateLayout();
                var image = new RenderTargetBitmap(760, 670, 96, 96, PixelFormats.Pbgra32);
                image.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = File.Create(args[1])) encoder.Save(stream);
                window.Close();
                return;
            }
            app.Run(new MainWindow());
        }
    }

    public sealed class MainWindow : Window
    {
        private static readonly HashSet<string> InputExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".3fr", ".arw", ".cr2", ".cr3", ".crw", ".dng", ".erf", ".kdc", ".mos",
            ".mrw", ".nef", ".nrw", ".orf", ".pef", ".raf", ".raw", ".rw2", ".rwl",
            ".sr2", ".srf", ".srw", ".x3f", ".jpg", ".jpeg", ".png", ".tif", ".tiff"
        };

        private readonly List<string> files = new List<string>();
        private readonly ListBox fileList;
        private readonly TextBox outputBox;
        private readonly ComboBox formatBox;
        private readonly TextBlock statusText;
        private readonly ProgressBar progress;
        private readonly Button convertButton;
        private readonly Button clearButton;
        private readonly Image headerIcon;
        private readonly Rectangle dropOutline;
        private CancellationTokenSource cancellation;
        private Process activeProcess;

        public MainWindow()
        {
            Title = "像素转换 · RAW → JPG";
            Width = 760;
            Height = 670;
            MinWidth = 640;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Icon = BitmapFrame.Create(new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aperture.ico"), UriKind.Absolute));
            var xaml = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MainWindow.xaml"), Encoding.UTF8);
            var root = (Grid)System.Windows.Markup.XamlReader.Parse(xaml);
            Content = root;
            fileList = (ListBox)root.FindName("FileList");
            outputBox = (TextBox)root.FindName("OutputBox");
            formatBox = (ComboBox)root.FindName("FormatBox");
            statusText = (TextBlock)root.FindName("StatusText");
            progress = (ProgressBar)root.FindName("Progress");
            convertButton = (Button)root.FindName("ConvertButton");
            clearButton = (Button)root.FindName("ClearButton");
            headerIcon = (Image)root.FindName("HeaderIcon");
            headerIcon.Source = new BitmapImage(new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aperture-symbol.png"), UriKind.Absolute));
            dropOutline = (Rectangle)root.FindName("DropOutline");
            ((Button)root.FindName("AddFilesButton")).Click += AddFiles;
            ((Button)root.FindName("AddFolderButton")).Click += AddFolder;
            ((Button)root.FindName("BrowseOutputButton")).Click += BrowseOutput;
            clearButton.Click += ClearFiles;
            convertButton.Click += ConvertClick;
            var dropZone = (Border)root.FindName("DropZone");
            dropZone.PreviewDragOver += DragOverFiles;
            dropZone.Drop += DropFiles;
            dropZone.PreviewDragEnter += (sender, e) => SetDropOutline(e.Data.GetDataPresent(DataFormats.FileDrop));
            dropZone.PreviewDragLeave += (sender, e) => SetDropOutline(false);
            dropZone.AddHandler(UIElement.DropEvent, new DragEventHandler((sender, e) => SetDropOutline(false)), true);
            PreviewDragOver += DragOverFiles;
            Drop += DropFiles;
            Closing += (sender, e) => { if (cancellation != null) cancellation.Cancel(); KillActiveProcess(); };
            formatBox.SelectedIndex = 0;
            RefreshList();
        }

        private void AddFiles(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog {
                Title = "选择图片或 RAW 原图", Multiselect = true,
                Filter = "支持的图片|*.3fr;*.arw;*.cr2;*.cr3;*.crw;*.dng;*.erf;*.kdc;*.mos;*.mrw;*.nef;*.nrw;*.orf;*.pef;*.raf;*.raw;*.rw2;*.rwl;*.sr2;*.srf;*.srw;*.x3f;*.jpg;*.jpeg;*.png;*.tif;*.tiff|所有文件|*.*"
            };
            if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames);
        }

        private void AddFolder(object sender, RoutedEventArgs e)
        {
            using (var dialog = new Forms.FolderBrowserDialog { Description = "选择图片文件夹" })
                if (dialog.ShowDialog() == Forms.DialogResult.OK) AddPaths(new[] { dialog.SelectedPath });
        }

        private void BrowseOutput(object sender, RoutedEventArgs e)
        {
            using (var dialog = new Forms.FolderBrowserDialog { Description = "选择 JPG / 图片输出文件夹", SelectedPath = Directory.Exists(outputBox.Text) ? outputBox.Text : "" })
                if (dialog.ShowDialog() == Forms.DialogResult.OK) outputBox.Text = dialog.SelectedPath;
        }

        private void DragOverFiles(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void SetDropOutline(bool active)
        {
            dropOutline.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#C7CDD3" : "#3A4249"));
        }

        private void StartIconRotation()
        {
            var rotation = (RotateTransform)headerIcon.RenderTransform;
            rotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(10)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        private void StopIconRotation()
        {
            var rotation = (RotateTransform)headerIcon.RenderTransform;
            rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            rotation.Angle = 0;
        }

        private void ShowCompletionStatus(string message, int failed)
        {
            int marker = message.IndexOf("失败 ", StringComparison.Ordinal);
            if (marker < 0) { statusText.Text = message; return; }
            int numberStart = marker + "失败 ".Length;
            string count = failed.ToString();
            statusText.Inlines.Clear();
            statusText.Inlines.Add(new Run(message.Substring(0, numberStart)));
            statusText.Inlines.Add(new Run(count) { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0564A")) });
            statusText.Inlines.Add(new Run(message.Substring(numberStart + count.Length)));
        }

        private void DropFiles(object sender, DragEventArgs e)
        {
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) AddPaths(paths);
            e.Handled = true;
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            if (cancellation != null) return;
            var existing = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in EnumerateFiles(path))
                        if (InputExtensions.Contains(Path.GetExtension(file)) && existing.Add(file)) { files.Add(file); added++; }
                }
                else if (File.Exists(path) && InputExtensions.Contains(Path.GetExtension(path)) && existing.Add(path))
                { files.Add(path); added++; }
            }
            if (added > 0 && string.IsNullOrWhiteSpace(outputBox.Text))
                outputBox.Text = Path.Combine(Path.GetDirectoryName(files[0]), "Converted");
            RefreshList();
            statusText.Text = added > 0 ? "已添加 " + added + " 张图片" : "没有找到支持的图片";
        }

        private IEnumerable<string> EnumerateFiles(string folder)
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(Path.Combine(folder, "Converted")).TrimEnd(Path.DirectorySeparatorChar)
            };
            if (!string.IsNullOrWhiteSpace(outputBox.Text))
                try { excluded.Add(Path.GetFullPath(outputBox.Text).TrimEnd(Path.DirectorySeparatorChar)); } catch { }
            var pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                string[] paths;
                try { paths = Directory.GetFiles(current); } catch { continue; }
                foreach (var path in paths) yield return path;
                try { paths = Directory.GetDirectories(current); } catch { continue; }
                foreach (var path in paths)
                    if (!excluded.Contains(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar))) pending.Push(path);
            }
        }

        private void ClearFiles(object sender, RoutedEventArgs e)
        {
            files.Clear();
            RefreshList();
            statusText.Text = "列表已清空";
        }

        private void RefreshList()
        {
            fileList.ItemsSource = null;
            fileList.ItemsSource = files.Select((path, index) => string.Format("{0,3}.  {1}     {2}", index + 1, Path.GetFileName(path), Path.GetDirectoryName(path))).ToList();
            ((TextBlock)((Grid)Content).FindName("CountText")).Text = files.Count + " 个文件";
            convertButton.IsEnabled = files.Count > 0 || cancellation != null;
        }

        private async void ConvertClick(object sender, RoutedEventArgs e)
        {
            if (cancellation != null) { cancellation.Cancel(); KillActiveProcess(); return; }
            if (files.Count == 0) return;
            var outputFolder = outputBox.Text.Trim();
            if (outputFolder.Length == 0) { MessageBox.Show(this, "请选择输出文件夹。", "需要输出位置"); return; }
            var engine = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "rawtherapee-cli.exe");
            if (!File.Exists(engine)) { MessageBox.Show(this, "转换引擎缺失，请重新解压完整软件包。", "无法转换"); return; }
            try { Directory.CreateDirectory(outputFolder); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "输出文件夹不可用"); return; }
            var format = ((ComboBoxItem)formatBox.SelectedItem).Tag.ToString();
            var queue = files.ToArray();
            cancellation = new CancellationTokenSource();
            StartIconRotation();
            convertButton.Content = "取消转换";
            clearButton.IsEnabled = false;
            formatBox.IsEnabled = false;
            progress.Value = 0;
            progress.Maximum = queue.Length;
            int success = 0, failed = 0;
            var failures = new List<string>();
            try
            {
                for (int i = 0; i < queue.Length; i++)
                {
                    if (cancellation.IsCancellationRequested) break;
                    var input = queue[i];
                    statusText.Text = string.Format("正在转换 {0}/{1}：{2}", i + 1, queue.Length, Path.GetFileName(input));
                    var destination = UniquePath(outputFolder, Path.GetFileNameWithoutExtension(input), format, input);
                    var result = await Task.Run(() => ConvertOne(engine, input, destination, format, cancellation.Token));
                    if (cancellation.IsCancellationRequested) break;
                    if (result == null) success++;
                    else { failed++; failures.Add(Path.GetFileName(input) + "：" + result); }
                    progress.Value = i + 1;
                }
                if (cancellation.IsCancellationRequested) ShowCompletionStatus(string.Format("已取消 · 成功 {0} · 失败 {1}", success, failed), failed);
                else ShowCompletionStatus(string.Format("完成 · 成功 {0} · 失败 {1} · 保存于 {2}", success, failed, outputFolder), failed);
                if (failures.Count > 0) MessageBox.Show(this, string.Join("\n", failures.Take(12)), "转换失败的文件");
            }
            finally
            {
                StopIconRotation();
                cancellation.Dispose(); cancellation = null; activeProcess = null;
                convertButton.Content = "开始转换";
                clearButton.IsEnabled = true; formatBox.IsEnabled = true;
                RefreshList();
            }
        }

        private static string UniquePath(string folder, string baseName, string format, string input)
        {
            var extension = "." + format;
            var path = Path.Combine(folder, baseName + extension);
            int counter = 2;
            while (File.Exists(path) || string.Equals(Path.GetFullPath(path), Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(folder, baseName + "_" + counter++ + extension);
            return path;
        }

        private string ConvertOne(string engine, string input, string destination, string format, CancellationToken token)
        {
            var sourceFormat = Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
            if (sourceFormat == format || (format == "jpg" && sourceFormat == "jpeg") || (format == "tif" && sourceFormat == "tiff"))
            {
                try { File.Copy(input, destination); return null; }
                catch (Exception ex) { return ex.Message; }
            }
            var arguments = new List<string> { "-q" };
            if (sourceFormat != "jpg" && sourceFormat != "jpeg" && sourceFormat != "png" && sourceFormat != "tif" && sourceFormat != "tiff")
            {
                var profile = Path.Combine(Path.GetDirectoryName(engine), "profiles", "Auto-Matched Curve - ISO Low.pp3");
                if (!File.Exists(profile)) return "RAW 显影配置缺失";
                arguments.Add("-p"); arguments.Add(Quote(profile));
            }
            arguments.Add("-s");
            if (format == "jpg") { arguments.Add("-j100"); arguments.Add("-js3"); }
            else if (format == "png") { arguments.Add("-n"); arguments.Add("-b16"); }
            else { arguments.Add("-tz"); arguments.Add("-b16"); }
            arguments.Add("-o"); arguments.Add(Quote(destination));
            arguments.Add("-c"); arguments.Add(Quote(input));
            var psi = new ProcessStartInfo(engine, string.Join(" ", arguments)) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(engine)
            };
            var output = new StringBuilder();
            try
            {
                using (var process = new Process { StartInfo = psi })
                {
                    activeProcess = process;
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                    process.Start();
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    while (!process.WaitForExit(200))
                    {
                        if (token.IsCancellationRequested) { try { process.Kill(); } catch { } break; }
                    }
                    process.WaitForExit();
                    activeProcess = null;
                    if (token.IsCancellationRequested) { if (File.Exists(destination)) File.Delete(destination); return "已取消"; }
                    if (process.ExitCode == 0 && File.Exists(destination) && new FileInfo(destination).Length > 0) return null;
                    if (File.Exists(destination)) File.Delete(destination);
                    var detail = output.ToString().Trim();
                    return detail.Length > 180 ? detail.Substring(detail.Length - 180) : (detail.Length > 0 ? detail : "引擎未生成文件");
                }
            }
            catch (Exception ex)
            {
                activeProcess = null;
                try { if (File.Exists(destination)) File.Delete(destination); } catch { }
                return ex.Message;
            }
        }

        private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
        private void KillActiveProcess() { try { if (activeProcess != null && !activeProcess.HasExited) activeProcess.Kill(); } catch { } }
    }
}
