using System;
using System.IO;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace LemonAID;

public sealed partial class MainWindow : Window
{
    public static MainWindow? CurrentInstance { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        CurrentInstance = this;

        ExtendsContentIntoTitleBar = true;
        AppWindow.Resize(new SizeInt32(960, 640));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }

        RootFrame.Navigate(typeof(MainPage));
    }

    public void SetAlwaysOnTop(bool pin)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = pin;
        }
    }

    public static void SpawnFloatingFileWindow(string filePath)
    {
        var floatWin = new Window
        {
            Title = $"Float — {Path.GetFileName(filePath)}",
            ExtendsContentIntoTitleBar = true
        };

        floatWin.AppWindow.Resize(new SizeInt32(360, 320));
        if (floatWin.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
        }

        var root = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 24, 24, 27)),
            Padding = new Thickness(16),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        var header = new TextBlock
        {
            Text = $"📌 {Path.GetFileName(filePath)}",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 204, 21)),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 12)
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")
        {
            var img = new Image
            {
                Source = new BitmapImage(new Uri(filePath)),
                Stretch = Stretch.Uniform
            };
            Grid.SetRow(img, 1);
            root.Children.Add(img);
        }
        else
        {
            var infoText = new TextBlock
            {
                Text = $"Path:\n{filePath}\n\nSize: {(File.Exists(filePath) ? new FileInfo(filePath).Length : 0):N0} bytes",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 255, 255, 255)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(infoText, 1);
            root.Children.Add(infoText);
        }

        var closeBtn = new Button
        {
            Content = "Dismiss Float",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 12, 0, 0)
        };
        closeBtn.Click += (_, _) => floatWin.Close();
        Grid.SetRow(closeBtn, 2);
        root.Children.Add(closeBtn);

        floatWin.Content = root;
        floatWin.Activate();
    }
}
