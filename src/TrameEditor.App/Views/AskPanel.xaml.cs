using System.Windows;
using System.Windows.Controls;
using TrameEditor.App.ViewModels;

namespace TrameEditor.App.Views;

/// <summary>
/// Il pannello dell'assistente, uguale per i PDF e per i documenti di testo:
/// cambia solo che cosa cita (pagine o righe) e dove porta il clic sulla
/// citazione, e quello lo decide il ViewModel che lo ospita.
/// </summary>
public partial class AskPanel : UserControl
{
    public AskPanel()
    {
        InitializeComponent();
    }

    private DocumentQaViewModel? ViewModel => DataContext as DocumentQaViewModel;

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsWindow.ShowEditor();

    private void Reference_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: int reference })
            ViewModel?.RequestReference(reference);
    }
}
