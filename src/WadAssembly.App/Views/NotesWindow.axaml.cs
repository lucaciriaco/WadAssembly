using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using WadAssembly.App.Services;

namespace WadAssembly.App.Views;

/// <summary>Modal editor for the per-slot additional notes. The notes are free text
/// stored in the project JSON file (not written to the generated PWAD).</summary>
public partial class NotesWindow : Window
{
    /// <summary>True when the notes were confirmed with Aceptar.</summary>
    public bool Accepted { get; private set; }

    /// <summary>The notes text confirmed with Aceptar.</summary>
    public string Notes { get; private set; } = "";

    /// <summary>Required by the Avalonia runtime loader; use the parameterized constructor.</summary>
    public NotesWindow()
    {
        InitializeComponent();
    }

    public NotesWindow(string slotName, string currentNotes)
    {
        InitializeComponent();

        var header = this.FindControl<TextBlock>("NotesHeader")!;
        header.Text = string.Format(LanguageService.GetString("Notes.SlotLabel"), slotName);

        var notesBox = this.FindControl<TextBox>("NotesBox")!;
        notesBox.Text = currentNotes;
        Notes = currentNotes;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        var notesBox = this.FindControl<TextBox>("NotesBox")!;
        Accepted = true;
        Notes = notesBox.Text ?? "";
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}