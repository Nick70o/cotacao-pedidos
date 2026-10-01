using System.Text.Json;
using CotacaoPedidos.Dados;
using CotacaoPedidos.Negocio;

namespace CotacaoPedidos.Apresentacao;

/// <summary>
/// Monta as três camadas na inicialização: cria a camada de Dados (SQL Server + Google) e a entrega à
/// camada de Negócio. É o único ponto da Apresentação que conhece a camada de Dados - as telas só
/// conversam com <see cref="CotacaoService"/>.
/// </summary>
internal sealed class Aplicacao : IDisposable
{
    private const string VariavelConnectionString = "ETRADE_CONNECTION_STRING";
    private const string VariavelOperacaoIde = "ETRADE_OPERACAO_COTACAO_REALIZADA_IDE";

    private readonly PlanilhaGoogleRepository _planilhas;

    private Aplicacao(CotacaoService cotacao, PlanilhaGoogleRepository planilhas)
    {
        Cotacao = cotacao;
        _planilhas = planilhas;
    }

    public CotacaoService Cotacao { get; }

    /// <summary>Na primeira execução abre o navegador para o login no Google; depois reaproveita o token salvo.</summary>
    public static async Task<Aplicacao> IniciarAsync(CancellationToken ct = default)
    {
        var baseDir = AppContext.BaseDirectory;
        var configuracao = LerConfiguracao(Path.Combine(baseDir, "appsettings.json"));

        // Confere o banco antes de abrir o Google: se a configuração estiver errada, nada é alterado.
        var etrade = new ETradeRepository(configuracao.ConnectionString, configuracao.OperacaoCotacaoRealizadaIde);
        await etrade.ValidarConfiguracaoAsync(ct);

        var planilhas = await PlanilhaGoogleRepositoryFactory.CriarAsync(
            credentialsJsonPath: Path.Combine(baseDir, "credentials.json"),
            tokenStoreDirectory: Path.Combine(baseDir, "token_store"),
            ct);

        return new Aplicacao(new CotacaoService(etrade, planilhas), planilhas);
    }

    private sealed record Configuracao(string ConnectionString, Guid OperacaoCotacaoRealizadaIde);

    /// <summary>
    /// Cada valor vem da variável de ambiente (recomendado em produção) ou, na falta dela, do
    /// appsettings.json ao lado do executável (seção "ETrade").
    /// </summary>
    private static Configuracao LerConfiguracao(string caminhoAppSettings)
    {
        JsonElement? etrade = null;
        JsonDocument? json = null;

        try
        {
            if (File.Exists(caminhoAppSettings))
            {
                json = JsonDocument.Parse(File.ReadAllText(caminhoAppSettings));
                if (json.RootElement.TryGetProperty("ETrade", out var secao))
                    etrade = secao;
            }

            string? Ler(string variavel, string propriedade)
            {
                var doAmbiente = Environment.GetEnvironmentVariable(variavel);
                if (!string.IsNullOrWhiteSpace(doAmbiente))
                    return doAmbiente;

                return etrade is { } e && e.TryGetProperty(propriedade, out var valor) ? valor.GetString() : null;
            }

            var connectionString = Ler(VariavelConnectionString, "ConnectionString");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "Connection string do ETrade não configurada. Defina a variável de ambiente " +
                    $"{VariavelConnectionString} ou o appsettings.json (seção \"ETrade\": \"ConnectionString\"). " +
                    "Use o appsettings.example.json como modelo.");

            if (!Guid.TryParse(Ler(VariavelOperacaoIde, "OperacaoCotacaoRealizadaIde"), out var operacaoIde) ||
                operacaoIde == Guid.Empty)
                throw new InvalidOperationException(
                    "Operacao__Ide da Operação 72 não configurado. Defina a variável de ambiente " +
                    $"{VariavelOperacaoIde} ou o appsettings.json (seção \"ETrade\": \"OperacaoCotacaoRealizadaIde\"). " +
                    "O valor é o Ide da Operação 72 na tabela Operacao do seu ETrade.");

            return new Configuracao(connectionString, operacaoIde);
        }
        finally
        {
            json?.Dispose();
        }
    }

    public void Dispose() => _planilhas.Dispose();
}
