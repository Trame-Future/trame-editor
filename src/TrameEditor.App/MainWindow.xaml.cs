using System.ComponentModel;
using System.IO;
using System.Windows;
using TrameEditor.App.ViewModels;

namespace TrameEditor.App;

public partial class MainWindow : Fluent.RibbonWindow
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += (_, _) =>
        {
            var restoredDrafts = _viewModel.TryRestoreDrafts();
            var args = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToList();
            if (args.Count > 0)
            {
                foreach (var path in args)
                    _viewModel.OpenPath(path);
            }
            else if (!restoredDrafts)
            {
                _viewModel.RestoreSession();
            }
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = !_viewModel.ConfirmCloseAll();
        if (!e.Cancel)
            _viewModel.SaveSession();
        base.OnClosing(e);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e) =>
        new AboutWindow { Owner = this }.ShowDialog();

    private void Help_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) =>
        new HelpWindow { Owner = this }.Show();

    private void Profile_Click(object sender, RoutedEventArgs e) =>
        ProfileWindow.ShowEditor();

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.ShowEditor();

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
                _viewModel.OpenPath(path);
        }
    }
}
