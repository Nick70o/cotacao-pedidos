using System.Diagnostics;

namespace CotacaoPedidos.Apresentacao;

internal static class Navegador
{
    /// <summary>Abre o endereço no navegador padrão do Windows.</summary>
    public static void Abrir(string url) =>
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
}
