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

    // ----- Riordino pagine con drag & drop delle miniature -----

    private System.Windows.Point _dragStart;
    private PdfPageViewModel? _dragPage;

    private void ThumbList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragPage = (e.OriginalSource as FrameworkElement)?.DataContext as PdfPageViewModel;
    }

    private void ThumbList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragPage is null)
            return;
        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var page = _dragPage;
        _dragPage = null;
        DragDrop.DoDragDrop(ThumbList, page, DragDropEffects.Move);
    }

    private void ThumbList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PdfPageViewModel)) is not PdfPageViewModel dropped ||
            DataContext is not PdfDocumentViewModel vm)
            return;
        var target = (e.OriginalSource as FrameworkElement)?.DataContext as PdfPageViewModel;
        vm.MovePage(dropped, target);
        e.Handled = true;
    }

    private void Matches_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems is [PdfSearchMatch match, ..])
            PageList.ScrollIntoView(match.Page);
    }

    private void Region_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not PdfTextRegionViewModel region ||
            DataContext is not PdfDocumentViewModel vm)
            return;

        if (vm.AnnotationTool == PdfAnnotationTool.Highlight)
            vm.HighlightRegionCommand.Execute(region);
        else
            vm.BeginEditCommand.Execute(region);
        e.Handled = true;
    }

    private async void PageImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PdfDocumentViewModel vm ||
            vm.AnnotationTool is PdfAnnotationTool.None or PdfAnnotationTool.Highlight ||
            ((FrameworkElement)sender).DataContext is not PdfPageViewModel page)
            return;

        var position = e.GetPosition((IInputElement)sender);
        e.Handled = true;
        await vm.HandlePageClickAsync(page, position.X / vm.Zoom, position.Y / vm.Zoom);
    }

    private void QaSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsWindow.ShowEditor();
        if (DataContext is PdfDocumentViewModel vm)
            vm.RetryQaCommand.Execute(null);
    }

    private void QaSource_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not int pageNumber ||
            DataContext is not PdfDocumentViewModel vm)
            return;
        var page = vm.Pages.FirstOrDefault(p => p.OriginalIndex + 1 == pageNumber);
        if (page is not null)
            PageList.ScrollIntoView(page);
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
