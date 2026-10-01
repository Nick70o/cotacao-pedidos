namespace CotacaoPedidos.Dados;

/// <summary>Layout fixo das abas Cotação e Controle (seção 9 da especificação).</summary>
internal static class LayoutPlanilha
{
    public const string AbaCotacao = "Cotação";
    public const string AbaControle = "Controle";

    public const int SheetIdCotacao = 0;
    public const int SheetIdControle = 1;

    // Colunas da aba Cotação: A=Código, B=Código EAN, C=Nome, D=Quantidade, E=Preço.
    public const int ColunaCodigo = 0;
    public const int ColunaCodigoEan = 1;
    public const int ColunaNome = 2;
    public const int ColunaQuantidade = 3;
    public const int ColunaPreco = 4;
    public const int QuantidadeColunasCotacao = 5;

    public static readonly string[] CabecalhoCotacao =
        ["Código", "Código EAN", "Nome", "Quantidade", "Preço"];

    // Aba Controle: layout Campo | Valor (seção 9.2 - "campos mínimos").
    public const string CampoMovimentoIde = "Movimento_Ide";
    public const string CampoFilial = "Filial";
    public const string CampoSequencia = "Sequência";
    public const string CampoSpreadsheetId = "SpreadsheetId";
    public const string CampoStatus = "Status";
    public const string CampoDataExportacao = "Data_Exportacao";
    public const string CampoDataImportacao = "Data_Importacao";

    /// <summary>Formato usado para gravar datas na aba Controle (ISO 8601, sem ambiguidade de local).</summary>
    public const string FormatoData = "o";

    // appProperties do arquivo no Drive: vínculo técnico que não depende do nome do arquivo. Quem tem o link
    // como editor consegue renomear a planilha; sem isto, o programa perderia a planilha e permitiria
    // exportar o mesmo pedido de novo. Só esta aplicação enxerga essas propriedades.
    public const string PropriedadeMovimentoIde = "movimentoIde";
    public const string PropriedadeFilial = "filial";
    public const string PropriedadeSequencia = "sequencia";

    public const string MimeTypePlanilha = "application/vnd.google-apps.spreadsheet";
}
