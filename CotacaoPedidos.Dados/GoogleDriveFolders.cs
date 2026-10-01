using Google.Apis.Drive.v3;
using GoogleFile = Google.Apis.Drive.v3.Data.File;

namespace CotacaoPedidos.Dados;

/// <summary>Localiza uma pasta pelo nome ou cria se não existir. Usado só na inicialização.</summary>
internal static class GoogleDriveFolders
{
    private const string MimeTypePasta = "application/vnd.google-apps.folder";

    public static async Task<string> LocalizarOuCriarAsync(
        DriveService drive, string nome, string? pastaPaiId, CancellationToken ct)
    {
        var existente = await LocalizarAsync(drive, nome, pastaPaiId, ct);
        if (existente is not null)
            return existente;

        var metadados = new GoogleFile
        {
            Name = nome,
            MimeType = MimeTypePasta,
            Parents = pastaPaiId is null ? null : [pastaPaiId]
        };

        var criar = drive.Files.Create(metadados);
        criar.Fields = "id";
        var criada = await criar.ExecuteAsync(ct);
        return criada.Id;
    }

    private static async Task<string?> LocalizarAsync(
        DriveService drive, string nome, string? pastaPaiId, CancellationToken ct)
    {
        var nomeEscapado = nome.Replace("'", "\\'");
        var consulta = $"name = '{nomeEscapado}' and mimeType = '{MimeTypePasta}' and trashed = false";
        if (pastaPaiId is not null)
            consulta += $" and '{pastaPaiId}' in parents";

        var listar = drive.Files.List();
        listar.Q = consulta;
        listar.Fields = "files(id, name)";
        listar.Spaces = "drive";
        listar.PageSize = 5;

        var resultado = await listar.ExecuteAsync(ct);
        return resultado.Files?.FirstOrDefault()?.Id;
    }
}
