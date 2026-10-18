using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RGVMonitor.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace RGVMonitor.App;

/// Configuración: versión completa de la app y copia de seguridad del historial.
public sealed partial class SettingsPage : Page
{
    private readonly HistoryBackup _backup = new();
    private nint _hwnd;

    public SettingsPage()
    {
        InitializeComponent();

        VersionLine.Text = $"Versión {AppVersion.Number}";
        var built = AppVersion.BuiltAt is { } at ? $"Compilada el {at:dd/MM/yyyy HH:mm}" : "";
        BuildLine.Text = AppVersion.Commit.Length > 0
            ? $"Commit {AppVersion.Commit}" + (built.Length > 0 ? $"\n{built}" : "")
            : built;
        DataFolderText.Text = Path.GetDirectoryName(_backup.BackupsDirectory);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is Window window)
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        RefreshCounts();
    }

    private void RefreshCounts()
    {
        var c = _backup.CurrentCounts();
        CountsText.Text = c.Players == 0 && c.Notes == 0
            ? "Todavía no hay jugadores guardados."
            : $"{c.Players:N0} jugadores · {c.Encounters:N0} encuentros · {c.Notes:N0} notas";
    }

    private void CopyVersion_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText($"RG Valorant Monitor {AppVersion.Informational}");
        Clipboard.SetContent(package);
        CopyVersionLabel.Text = "Copiada";
        InteractionSounds.Current.Play(InteractionCue.Toggle, 0.6);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = DataFolderText.Text;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"RGVMonitor-historial-{DateTime.Now:yyyy-MM-dd}",
        };
        picker.FileTypeChoices.Add("Copia de RGVMonitor", new List<string> { ".zip" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        await RunBusy(async () =>
        {
            var counts = await Task.Run(() => _backup.Export(file.Path, AppVersion.Informational));
            ShowResult(InfoBarSeverity.Success, "Historial exportado",
                $"{counts.Players:N0} jugadores, {counts.Encounters:N0} encuentros y {counts.Notes:N0} notas en {file.Name}.");
        });
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".zip");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "¿Importar este historial?",
            Content = $"Se añadirá lo que traiga {file.Name} a tu historial actual. No se borra nada, y antes se guarda una copia automática de lo que tienes ahora.",
            PrimaryButtonText = "Importar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        await RunBusy(async () =>
        {
            var r = await Task.Run(() => _backup.Import(file.Path));
            RefreshCounts();
            var added = r.NewPlayers + r.NewEncounters + r.NewOrUpdatedNotes == 0
                ? "No había nada nuevo: todo lo de la copia ya estaba en tu historial."
                : $"{r.NewPlayers:N0} jugadores nuevos, {r.NewEncounters:N0} encuentros nuevos y {r.NewOrUpdatedNotes:N0} notas añadidas o actualizadas.";
            ShowResult(InfoBarSeverity.Success, "Historial importado",
                $"{added}\nCopia de lo anterior: {r.SafetyCopyPath}");
        });
    }

    private async Task RunBusy(Func<Task> action)
    {
        ExportButton.IsEnabled = ImportButton.IsEnabled = false;
        ResultBar.IsOpen = false;
        try
        {
            await action();
            InteractionSounds.Current.Play(InteractionCue.Success, 0.6);
        }
        catch (InvalidDataException ex)
        {
            ShowResult(InfoBarSeverity.Error, "No se pudo importar", ex.Message + " Tu historial no se ha modificado.");
            InteractionSounds.Current.Play(InteractionCue.Error);
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, "Algo falló", ex.Message);
            InteractionSounds.Current.Play(InteractionCue.Error);
        }
        finally
        {
            ExportButton.IsEnabled = ImportButton.IsEnabled = true;
        }
    }

    private void ShowResult(InfoBarSeverity severity, string title, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Title = title;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }
}
