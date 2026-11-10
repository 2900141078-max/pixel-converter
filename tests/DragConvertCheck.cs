using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using RawJpgStudio;

// Integration check: sends the same FileDrop data handled by the app, then clicks Convert.
public static class DragConvertCheck
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("input1 input2 outputFolder reportPath");
        File.WriteAllText(args[3], "start\n");
        var app = new Application();
        app.DispatcherUnhandledException += (sender, e) => { File.AppendAllText(args[3], "unhandled=" + e.Exception.GetType().FullName + ":" + e.Exception.Message + "\ninner=" + (e.Exception.InnerException == null ? "none" : e.Exception.InnerException.ToString()) + "\n"); e.Handled = true; app.Shutdown(2); };
        var window = new MainWindow { ShowInTaskbar = false, WindowState = WindowState.Minimized };
        File.AppendAllText(args[3], "window-created\n");
        var root = (Grid)window.Content;
        var status = (TextBlock)root.FindName("StatusText");
        var output = (TextBox)root.FindName("OutputBox");
        var count = (TextBlock)root.FindName("CountText");
        var button = (Button)root.FindName("ConvertButton");
        var icon = (Image)root.FindName("HeaderIcon");
        var dropZone = (Border)root.FindName("DropZone");
        var outline = (Rectangle)root.FindName("DropOutline");
        bool sawRotation = false;
        string hoverColor = "untested", restColor = "untested";
        var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        var deadline = DateTime.UtcNow.AddMinutes(3);
        watch.Tick += (sender, eventArgs) =>
        {
            if (status.Text.StartsWith("正在转换") && Math.Abs(((RotateTransform)icon.RenderTransform).Angle) > 0.5)
                sawRotation = true;
            if (!status.Text.StartsWith("完成") && DateTime.UtcNow < deadline) return;
            watch.Stop();
            var a = Path.Combine(args[2], Path.GetFileNameWithoutExtension(args[0]) + ".jpg");
            var b = Path.Combine(args[2], Path.GetFileNameWithoutExtension(args[1]) + ".jpg");
            var coloredRun = status.Inlines.OfType<Run>().Skip(1).FirstOrDefault();
            File.WriteAllLines(args[3], new[] {
                "status=" + status.Text,
                "count=" + count.Text,
                "output=" + args[2],
                "rotating_during=" + sawRotation,
                "rotation_after=" + ((RotateTransform)icon.RenderTransform).Angle,
                "drop_hover=" + hoverColor,
                "drop_rest=" + restColor,
                "failure_count_color=" + (coloredRun == null ? "none" : ((SolidColorBrush)coloredRun.Foreground).Color.ToString()),
                "file1=" + a + "|" + (File.Exists(a) ? new FileInfo(a).Length.ToString() : "missing"),
                "file2=" + b + "|" + (File.Exists(b) ? new FileInfo(b).Length.ToString() : "missing")
            });
            window.Close();
        };
        window.Loaded += (sender, eventArgs) =>
        {
            File.AppendAllText(args[3], "loaded\n");
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { args[0], args[1] });
            var ctor = typeof(DragEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point) }, null);
            var drag = (DragEventArgs)ctor.Invoke(new object[] { data, DragDropKeyStates.None, DragDropEffects.Copy, window, new Point(50, 50) });
            drag.RoutedEvent = UIElement.PreviewDragEnterEvent;
            dropZone.RaiseEvent(drag);
            hoverColor = ((SolidColorBrush)outline.Stroke).Color.ToString();
            drag.RoutedEvent = UIElement.PreviewDragLeaveEvent;
            dropZone.RaiseEvent(drag);
            restColor = ((SolidColorBrush)outline.Stroke).Color.ToString();
            drag.RoutedEvent = UIElement.DropEvent;
            File.AppendAllText(args[3], "drag-created\n");
            typeof(MainWindow).GetMethod("DropFiles", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { window, drag });
            File.AppendAllText(args[3], "drop-complete\n");
            output.Text = args[2];
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            File.AppendAllText(args[3], "clicked\n");
            watch.Start();
        };
        app.Run(window);
    }
}
