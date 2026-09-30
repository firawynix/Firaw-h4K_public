using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace FirawH4K;

internal static class UpdateService
{
    private const string ManifestUrl = "https://h4k.firawynix.com.br/downloads/release.json";
    private const string DownloadPage = "https://h4k.firawynix.com.br/#downloads";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    public static async Task CheckAsync(bool showNoUpdate, Func<bool> effectsActive)
    {
        // Store packages are updated by Microsoft, not by the website installer.
        var length = 0;
        if (GetCurrentPackageFullName(ref length, null) != 15700)
        {
            if (showNoUpdate) MessageBox.Show("Esta edição é atualizada pela Microsoft Store.", "Firaw h4K");
            return;
        }
        try
        {
            using var response = await Client.GetAsync(ManifestUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 65536) throw new InvalidOperationException("Manifesto muito grande.");
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 5 });
            var publishedText = document.RootElement.GetProperty("version").GetString();
            if (!Version.TryParse(publishedText, out var published) || published.Build < 0)
                throw new InvalidOperationException("Versão de atualização inválida.");
            var installed = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            if (published <= installed)
            {
                if (showNoUpdate) MessageBox.Show("Você já está na versão mais recente.", "Firaw h4K");
                return;
            }
            if (effectsActive())
            {
                if (showNoUpdate) MessageBox.Show("Há uma atualização. Encerre os efeitos antes de instalar.", "Firaw h4K");
                return;
            }
            if (MessageBox.Show($"A versão {published} está disponível. Abrir a página oficial de download? Feche o Firaw h4K antes de instalar.",
                "Atualização Firaw h4K", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(DownloadPage) { UseShellExecute = true });
        }
        catch (Exception)
        {
            if (showNoUpdate) MessageBox.Show("Não foi possível verificar atualizações agora. Tente novamente mais tarde.", "Firaw h4K");
        }
    }
}
