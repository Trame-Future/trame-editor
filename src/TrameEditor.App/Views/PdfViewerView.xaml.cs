using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TrameEditor.App.ViewModels;

namespace TrameEditor.App.Views;

public partial class PdfViewerView : UserControl
{
    public PdfViewerView()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PdfPageViewModel page)
            await page.EnsureImageAsync();
    }

    private async void Thumb_Loaded(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PdfPageViewModel page)
            await page.EnsureThumbnailAsync();
    }

    private void ThumbList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0)
            PageList.ScrollIntoView(e.AddedItems[0]);
    }

    private void Matches_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems is [PdfSearchMatch match, ..])
            PageList.ScrollIntoView(match.Page);
    }

    private void Region_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PdfTextRegionViewModel region &&
            DataContext is PdfDocumentViewModel vm)
        {
            vm.BeginEditCommand.Execute(region);
            e.Handled = true;
        }
    }

    private void PageList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control ||
            DataContext is not PdfDocumentViewModel vm)
            return;

        if (e.Delta > 0)
            vm.ZoomInCommand.Execute(null);
        else
            vm.ZoomOutCommand.Execute(null);
        e.Handled = true;
    }
}
