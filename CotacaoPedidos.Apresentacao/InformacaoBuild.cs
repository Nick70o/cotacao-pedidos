using System.Globalization;
using System.Reflection;

namespace CotacaoPedidos.Apresentacao;

/// <summary>
/// Versão e data da compilação, exibidas no rodapé da tela. Servem para saber, sem abrir arquivos, qual
/// versão do programa está rodando (por exemplo, ao conferir se uma correção já chegou no computador do comprador).
/// </summary>
internal static class InformacaoBuild
{
    /// <summary>Versão do projeto (propriedade Version do .csproj), por exemplo "1.0.0".</summary>
    public static string Versao { get; }

    /// <summary>Data e hora em que o programa foi compilado.</summary>
    public static DateTime? Compilacao { get; }

    /// <summary>Texto do rodapé, por exemplo "Versão 1.0.0 · Build 01/10/2026 09:12".</summary>
    public static string Texto { get; }

    /// <summary>Detalhes mostrados ao passar o mouse sobre o rodapé.</summary>
    public static string Detalhes { get; }

    static InformacaoBuild()
    {
        var assembly = typeof(InformacaoBuild).Assembly;

        // O SDK pode acrescentar "+<hash do commit>" à versão informativa; só o número interessa na tela.
        var informativa = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Versao = informativa?.Split('+')[0] ?? assembly.GetName().Version?.ToString(3) ?? "?";

        var carimbo = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "DataCompilacao")?.Value;
        if (DateTime.TryParseExact(carimbo, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
            Compilacao = data;

        var compilacaoTexto = Compilacao?.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));

        Texto = compilacaoTexto is null
            ? $"Versão {Versao}"
            : $"Versão {Versao} · Build {compilacaoTexto}";

        Detalhes = $"Versão: {Versao}\nCompilado em: {compilacaoTexto ?? "não informado"}\n" +
                   $".NET: {Environment.Version}";
    }
}
