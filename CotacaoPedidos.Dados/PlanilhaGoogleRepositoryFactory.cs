using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Util.Store;

namespace CotacaoPedidos.Dados;

/// <summary>
/// Cria o <see cref="PlanilhaGoogleRepository"/> já autenticado e com as pastas do Drive prontas.
/// Chame uma única vez, no início do programa: a autorização (login no navegador) só acontece
/// na primeira execução; depois disso o token fica salvo em <paramref name="tokenStoreDirectory"/>.
/// </summary>
public static class PlanilhaGoogleRepositoryFactory
{
    // O ApplicationName vira o cabeçalho HTTP User-Agent, que não aceita acentos/espaços.
    private const string NomeAplicacao = "CotacaoPedidos";
    private const string NomePastaRaiz = "Cotação de Pedidos";
    private const string NomePastaArquivados = "Registros Arquivados";

    public static async Task<PlanilhaGoogleRepository> CriarAsync(
        string credentialsJsonPath,
        string tokenStoreDirectory,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialsJsonPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenStoreDirectory);

        if (!File.Exists(credentialsJsonPath))
            throw new FileNotFoundException(
                "Arquivo credentials.json não encontrado. Baixe-o no Google Cloud Console " +
                "(ID do cliente OAuth do tipo 'Aplicativo para computador').", credentialsJsonPath);

        GoogleClientSecrets clientSecrets;
        await using (var stream = new FileStream(credentialsJsonPath, FileMode.Open, FileAccess.Read))
            clientSecrets = await GoogleClientSecrets.FromStreamAsync(stream, ct);

        var escopos = new[] { SheetsService.Scope.Spreadsheets, DriveService.Scope.DriveFile };

        // Abre o navegador para o login na primeira vez; nas próximas, reaproveita o token salvo em disco.
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets.Secrets,
            escopos,
            user: "cotacao-pedidos",
            taskCancellationToken: ct,
            dataStore: new FileDataStore(tokenStoreDirectory, fullPath: true));

        var initializer = new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = NomeAplicacao
        };

        var sheetsService = new SheetsService(initializer);
        var driveService = new DriveService(initializer);

        try
        {
            var pastaRaizId = await GoogleDriveFolders.LocalizarOuCriarAsync(
                driveService, NomePastaRaiz, pastaPaiId: null, ct);

            var pastaArquivadosId = await GoogleDriveFolders.LocalizarOuCriarAsync(
                driveService, NomePastaArquivados, pastaPaiId: pastaRaizId, ct);

            return new PlanilhaGoogleRepository(sheetsService, driveService, pastaRaizId, pastaArquivadosId);
        }
        catch
        {
            sheetsService.Dispose();
            driveService.Dispose();
            throw;
        }
    }
}
