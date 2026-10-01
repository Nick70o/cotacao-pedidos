using System.Globalization;
using System.Text.RegularExpressions;
using CotacaoPedidos.Negocio;
using Google.Apis.Drive.v3;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using GoogleFile = Google.Apis.Drive.v3.Data.File;

namespace CotacaoPedidos.Dados;

/// <summary>
/// Operações da planilha de cotação no Google Sheets/Drive (seções 8, 9, 10, 17, 19 e 20 da especificação).
/// Uma instância é criada uma vez, via <see cref="PlanilhaGoogleRepositoryFactory"/>, e reutilizada.
/// </summary>
public sealed partial class PlanilhaGoogleRepository : IPlanilhaRepository, IDisposable
{
    private readonly SheetsService _sheets;
    private readonly DriveService _drive;
    private readonly string _pastaRaizId;
    private readonly string _pastaArquivadosId;

    internal PlanilhaGoogleRepository(
        SheetsService sheets, DriveService drive, string pastaRaizId, string pastaArquivadosId)
    {
        _sheets = sheets;
        _drive = drive;
        _pastaRaizId = pastaRaizId;
        _pastaArquivadosId = pastaArquivadosId;
    }

    // ---------------------------------------------------------------------------------------------
    // Localizar (seção 7): encontrar a planilha ativa de um pedido, se existir
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Procura, na pasta ativa, a planilha do pedido pelo vínculo gravado no arquivo (appProperties) e,
    /// para planilhas antigas, pelos três nomes possíveis. Confirma o vínculo pela aba Controle
    /// (seção 4: nunca confiar só no nome). Retorna null se não existir.
    /// </summary>
    public async Task<PlanilhaVinculada?> LocalizarPlanilhaAtivaAsync(
        PedidoMovimento pedido, CancellationToken ct = default)
    {
        var candidatos = await ListarNaPastaAtivaAsync(
            $"appProperties has {{ key='{LayoutPlanilha.PropriedadeMovimentoIde}' and value='{pedido.Ide:D}' }}", ct);

        // Planilhas criadas antes do vínculo existir só podem ser encontradas pelo nome.
        var encontradaPeloNome = candidatos.Count == 0;
        if (encontradaPeloNome)
        {
            var nomes = NomeArquivoCotacao.TodosOsNomesPossiveis(pedido.Filial, pedido.Sequencia)
                .Select(n => $"name = '{EscaparConsulta(n)}'");
            candidatos = await ListarNaPastaAtivaAsync($"({string.Join(" or ", nomes)})", ct);
        }

        if (candidatos.Count == 0)
            return null;

        if (candidatos.Count > 1)
            throw new PlanilhaInconsistenteException(
                $"Foram encontradas {candidatos.Count} planilhas ativas para a Filial {pedido.Filial} - " +
                $"Pedido {pedido.Sequencia}. Deveria existir no máximo uma. Verifique a pasta " +
                "'Cotação de Pedidos' manualmente.");

        var arquivo = candidatos[0];
        var controle = await LerControleAsync(arquivo.Id, ct);

        if (controle.Filial != pedido.Filial || controle.Sequencia != pedido.Sequencia)
            throw new PlanilhaInconsistenteException(
                $"A planilha '{arquivo.Name}' está vinculada ao pedido, mas a aba Controle " +
                "registra Filial/Sequência diferentes. Verifique manualmente antes de continuar.");

        if (encontradaPeloNome && controle.MovimentoIde == pedido.Ide)
            await TentarGravarVinculoAsync(arquivo.Id, pedido, ct);

        return new PlanilhaVinculada(arquivo.Id, arquivo.Name, controle);
    }

    private async Task<List<GoogleFile>> ListarNaPastaAtivaAsync(string condicao, CancellationToken ct)
    {
        var listar = _drive.Files.List();
        listar.Q = $"'{_pastaRaizId}' in parents and trashed = false and {condicao}";
        listar.Fields = "files(id, name)";
        listar.Spaces = "drive";
        listar.PageSize = 10;

        var resultado = await listar.ExecuteAsync(ct);
        return resultado.Files?.ToList() ?? [];
    }

    private static string EscaparConsulta(string texto) => texto.Replace("\\", "\\\\").Replace("'", "\\'");

    /// <summary>Migra uma planilha antiga para o vínculo por appProperties. Falhar aqui não impede o uso.</summary>
    private async Task TentarGravarVinculoAsync(string fileId, PedidoMovimento pedido, CancellationToken ct)
    {
        try
        {
            var atualizar = _drive.Files.Update(MetadadosDeVinculo(pedido), fileId);
            atualizar.Fields = "id";
            await atualizar.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException)
        {
            // A planilha continua localizável pelo nome; tenta de novo na próxima consulta.
        }
    }

    private static GoogleFile MetadadosDeVinculo(PedidoMovimento pedido) => new()
    {
        // Quem edita pelo link não pode compartilhar com outras pessoas nem alterar permissões.
        WritersCanShare = false,
        AppProperties = new Dictionary<string, string>
        {
            [LayoutPlanilha.PropriedadeMovimentoIde] = pedido.Ide.ToString("D"),
            [LayoutPlanilha.PropriedadeFilial] = pedido.Filial.ToString(CultureInfo.InvariantCulture),
            [LayoutPlanilha.PropriedadeSequencia] = pedido.Sequencia.ToString(CultureInfo.InvariantCulture)
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Exportar (seção 8): criar a planilha de um pedido novo
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Cria a planilha de cotação: abas Cotação e Controle, proteções, validação de preço e
    /// compartilhamento por link. Não verifica duplicidade - isso é regra da camada de Negócio.
    /// </summary>
    public async Task<PlanilhaVinculada> CriarPlanilhaAsync(
        PedidoMovimento pedido, IReadOnlyList<ItemPedido> itens, CancellationToken ct = default)
    {
        var nomeArquivo = NomeArquivoCotacao.Gerar(NomesStatus.AguardandoCotacao, pedido.Filial, pedido.Sequencia);

        var spreadsheet = new Spreadsheet
        {
            Properties = new SpreadsheetProperties { Title = nomeArquivo },
            Sheets =
            [
                new Sheet
                {
                    Properties = new SheetProperties
                    {
                        Title = LayoutPlanilha.AbaCotacao,
                        SheetId = LayoutPlanilha.SheetIdCotacao,
                        GridProperties = new GridProperties { FrozenRowCount = 1 }
                    }
                },
                new Sheet
                {
                    Properties = new SheetProperties
                    {
                        Title = LayoutPlanilha.AbaControle,
                        SheetId = LayoutPlanilha.SheetIdControle,
                        Hidden = true
                    }
                }
            ]
        };

        var criado = await _sheets.Spreadsheets.Create(spreadsheet).ExecuteAsync(ct);
        var spreadsheetId = criado.SpreadsheetId;

        try
        {
            // Move para a pasta da aplicação e grava o vínculo técnico no arquivo, numa chamada só.
            await MoverParaPastaAsync(
                spreadsheetId, destinoId: _pastaRaizId, origemId: "root", MetadadosDeVinculo(pedido), ct);

            var controle = new ControleCotacao(
                MovimentoIde: pedido.Ide,
                Filial: pedido.Filial,
                Sequencia: pedido.Sequencia,
                SpreadsheetId: spreadsheetId,
                Status: NomesStatus.AguardandoCotacao,
                DataExportacao: DateTime.Now,
                DataImportacao: null);

            await EscreverItensAsync(spreadsheetId, itens, ct);
            await EscreverControleAsync(spreadsheetId, controle, ct);
            await AplicarProtecoesEValidacaoAsync(spreadsheetId, itens.Count, ct);
            await CompartilharComQualquerPessoaComLinkAsync(spreadsheetId, somenteLeitura: false, ct);

            return new PlanilhaVinculada(spreadsheetId, nomeArquivo, controle);
        }
        catch
        {
            // A planilha ficou pela metade: melhor apagar do que deixar um arquivo incompleto no Drive.
            await TentarExcluirAsync(spreadsheetId);
            throw;
        }
    }

    private async Task EscreverItensAsync(
        string spreadsheetId, IReadOnlyList<ItemPedido> itens, CancellationToken ct)
    {
        var linhas = new List<IList<object>> { LayoutPlanilha.CabecalhoCotacao };

        // Preço fica de fora: começa vazio (seção 12: vazio é diferente de zero).
        foreach (var item in itens)
            linhas.Add([item.Codigo, item.CodigoEan, item.Nome, item.Quantidade]);

        await GravarValoresAsync(spreadsheetId, $"{LayoutPlanilha.AbaCotacao}!A1", linhas, ct);
    }

    private async Task EscreverControleAsync(string spreadsheetId, ControleCotacao controle, CancellationToken ct)
    {
        var linhas = new List<IList<object>>
        {
            new List<object> { LayoutPlanilha.CampoMovimentoIde, controle.MovimentoIde.ToString() },
            new List<object> { LayoutPlanilha.CampoFilial, controle.Filial },
            new List<object> { LayoutPlanilha.CampoSequencia, controle.Sequencia },
            new List<object> { LayoutPlanilha.CampoSpreadsheetId, controle.SpreadsheetId },
            new List<object> { LayoutPlanilha.CampoStatus, controle.Status },
            new List<object> { LayoutPlanilha.CampoDataExportacao, FormatarData(controle.DataExportacao) },
            new List<object> { LayoutPlanilha.CampoDataImportacao, FormatarData(controle.DataImportacao) }
        };

        await GravarValoresAsync(spreadsheetId, $"{LayoutPlanilha.AbaControle}!A1", linhas, ct);
    }

    private static string FormatarData(DateTime? data) =>
        data?.ToString(LayoutPlanilha.FormatoData, CultureInfo.InvariantCulture) ?? "";

    /// <summary>
    /// Grava como RAW (exatamente como enviado). Com USER_ENTERED o Sheets interpretava o conteúdo:
    /// um nome de produto começando com "=", "+" ou "-" virava fórmula, "1/2" virava data e códigos
    /// perdiam zeros à esquerda.
    /// </summary>
    private async Task GravarValoresAsync(
        string spreadsheetId, string range, IList<IList<object>> linhas, CancellationToken ct)
    {
        var atualizar = _sheets.Spreadsheets.Values.Update(new ValueRange { Values = linhas }, spreadsheetId, range);
        atualizar.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await atualizar.ExecuteAsync(ct);
    }

    /// <summary>
    /// Seções 9 e 20: a aba Cotação inteira fica protegida, com exceção das células de Preço dos itens.
    /// Assim o fornecedor não altera Código, Código EAN, Nome, Quantidade, cabeçalho, nem cria linhas ou
    /// colunas, nem renomeia/apaga a aba. A aba Controle fica oculta e protegida.
    /// </summary>
    private async Task AplicarProtecoesEValidacaoAsync(string spreadsheetId, int quantidadeItens, CancellationToken ct)
    {
        var celulasDePreco = new GridRange
        {
            SheetId = LayoutPlanilha.SheetIdCotacao,
            StartRowIndex = 1,
            EndRowIndex = quantidadeItens + 1, // +1 pelo cabeçalho
            StartColumnIndex = LayoutPlanilha.ColunaPreco,
            EndColumnIndex = LayoutPlanilha.ColunaPreco + 1
        };

        var requisicoes = new List<Request>
        {
            // Sem "Editors": só quem criou a planilha (a própria aplicação) edita a área protegida, mesmo com o
            // arquivo compartilhado como editor para "qualquer pessoa com o link".
            new()
            {
                AddProtectedRange = new AddProtectedRangeRequest
                {
                    ProtectedRange = new ProtectedRange
                    {
                        Range = new GridRange { SheetId = LayoutPlanilha.SheetIdCotacao },
                        UnprotectedRanges = [celulasDePreco],
                        Description = "Somente a coluna Preço pode ser alterada",
                        WarningOnly = false
                    }
                }
            },
            // Aba Controle inteira protegida (sem Range = SheetId sozinho protege a aba toda).
            new()
            {
                AddProtectedRange = new AddProtectedRangeRequest
                {
                    ProtectedRange = new ProtectedRange
                    {
                        Range = new GridRange { SheetId = LayoutPlanilha.SheetIdControle },
                        Description = "Uso interno da aplicação",
                        WarningOnly = false
                    }
                }
            },
            // Preço: só número >= 0. Célula vazia continua válida (não conta como preenchida).
            new()
            {
                SetDataValidation = new SetDataValidationRequest
                {
                    Range = celulasDePreco,
                    Rule = new DataValidationRule
                    {
                        Condition = new BooleanCondition
                        {
                            Type = "NUMBER_GREATER_THAN_EQ",
                            Values = [new ConditionValue { UserEnteredValue = "0" }]
                        },
                        Strict = true,
                        InputMessage = "Informe um preço numérico maior ou igual a zero."
                    }
                }
            },
            // Destaca as únicas células que o fornecedor pode preencher.
            new()
            {
                RepeatCell = new RepeatCellRequest
                {
                    Range = celulasDePreco,
                    Cell = new CellData
                    {
                        UserEnteredFormat = new CellFormat
                        {
                            BackgroundColor = new Color { Red = 1f, Green = 0.95f, Blue = 0.8f }
                        }
                    },
                    Fields = "userEnteredFormat.backgroundColor"
                }
            },
            new()
            {
                RepeatCell = new RepeatCellRequest
                {
                    Range = new GridRange { SheetId = LayoutPlanilha.SheetIdCotacao, StartRowIndex = 0, EndRowIndex = 1 },
                    Cell = new CellData { UserEnteredFormat = new CellFormat { TextFormat = new TextFormat { Bold = true } } },
                    Fields = "userEnteredFormat.textFormat.bold"
                }
            },
            new()
            {
                AutoResizeDimensions = new AutoResizeDimensionsRequest
                {
                    Dimensions = new DimensionRange
                    {
                        SheetId = LayoutPlanilha.SheetIdCotacao,
                        Dimension = "COLUMNS",
                        StartIndex = 0,
                        EndIndex = LayoutPlanilha.QuantidadeColunasCotacao
                    }
                }
            }
        };

        var lote = new BatchUpdateSpreadsheetRequest { Requests = requisicoes };
        await _sheets.Spreadsheets.BatchUpdate(lote, spreadsheetId).ExecuteAsync(ct);
    }

    // ---------------------------------------------------------------------------------------------
    // Localizar pedido / conferência: ler o estado atual da planilha
    // ---------------------------------------------------------------------------------------------

    /// <summary>Lê as linhas de itens da aba Cotação (sem o cabeçalho), como estão agora.</summary>
    public async Task<IReadOnlyList<LinhaCotacao>> LerLinhasAsync(string spreadsheetId, CancellationToken ct = default)
    {
        var obter = _sheets.Spreadsheets.Values.Get(spreadsheetId, $"{LayoutPlanilha.AbaCotacao}!A2:E");
        obter.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;

        var resposta = await obter.ExecuteAsync(ct);
        var valores = resposta.Values ?? [];
        var linhas = new List<LinhaCotacao>(valores.Count);

        for (var i = 0; i < valores.Count; i++)
        {
            var celulas = valores[i];
            var codigo = Texto(celulas, LayoutPlanilha.ColunaCodigo);
            var nome = Texto(celulas, LayoutPlanilha.ColunaNome);
            var (precoDigitado, preco) = LerPreco(Celula(celulas, LayoutPlanilha.ColunaPreco));

            if (string.IsNullOrWhiteSpace(codigo) && string.IsNullOrWhiteSpace(nome) && precoDigitado is null)
                continue; // linha em branco

            linhas.Add(new LinhaCotacao(
                NumeroLinha: i + 2, // +1 pelo índice começando em 0, +1 pelo cabeçalho
                Codigo: codigo,
                CodigoEan: Texto(celulas, LayoutPlanilha.ColunaCodigoEan),
                Nome: nome,
                Quantidade: Celula(celulas, LayoutPlanilha.ColunaQuantidade) switch
                {
                    double d => DeDouble(d),
                    long l => l,
                    _ => null
                },
                PrecoDigitado: precoDigitado,
                Preco: preco));
        }

        return linhas;
    }

    private static object? Celula(IList<object> linha, int coluna) => coluna < linha.Count ? linha[coluna] : null;

    private static string Texto(IList<object> linha, int coluna) =>
        Convert.ToString(Celula(linha, coluna), CultureInfo.InvariantCulture) ?? "";

    private static (string? Digitado, decimal? Valor) LerPreco(object? celula) => celula switch
    {
        null => (null, null),
        string s when string.IsNullOrWhiteSpace(s) => (null, null),
        double d => (d.ToString(CultureInfo.InvariantCulture), DeDouble(d)),
        long l => (l.ToString(CultureInfo.InvariantCulture), (decimal?)l),
        // Texto na coluna Preço (valor colado, que escapa da validação da célula) não é convertido por
        // adivinhação: "1.234" tanto pode ser mil duzentos e trinta e quatro quanto 1,234. Bloqueia a importação.
        _ => (Convert.ToString(celula, CultureInfo.InvariantCulture), null)
    };

    private static decimal? DeDouble(double valor)
    {
        if (double.IsNaN(valor) || double.IsInfinity(valor))
            return null;
        try { return (decimal)valor; }
        catch (OverflowException) { return null; }
    }

    private async Task<ControleCotacao> LerControleAsync(string spreadsheetId, CancellationToken ct)
    {
        var obter = _sheets.Spreadsheets.Values.Get(spreadsheetId, $"{LayoutPlanilha.AbaControle}!A:B");
        obter.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;

        var resposta = await obter.ExecuteAsync(ct);

        var campos = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var linha in resposta.Values ?? [])
        {
            if (linha.Count < 2)
                continue;
            campos[linha[0]?.ToString() ?? ""] = linha[1];
        }

        string Obter(string chave) =>
            Convert.ToString(campos.GetValueOrDefault(chave), CultureInfo.InvariantCulture) ?? "";

        if (!Guid.TryParse(Obter(LayoutPlanilha.CampoMovimentoIde), out var movimentoIde))
            throw new PlanilhaInconsistenteException(
                $"A aba Controle da planilha '{spreadsheetId}' está sem um Movimento_Ide válido.");

        return new ControleCotacao(
            MovimentoIde: movimentoIde,
            Filial: int.TryParse(Obter(LayoutPlanilha.CampoFilial), out var f) ? f : 0,
            Sequencia: int.TryParse(Obter(LayoutPlanilha.CampoSequencia), out var s) ? s : 0,
            SpreadsheetId: Obter(LayoutPlanilha.CampoSpreadsheetId),
            Status: Obter(LayoutPlanilha.CampoStatus),
            DataExportacao: ParaData(campos.GetValueOrDefault(LayoutPlanilha.CampoDataExportacao)),
            DataImportacao: ParaData(campos.GetValueOrDefault(LayoutPlanilha.CampoDataImportacao)));
    }

    private static DateTime? ParaData(object? valor) => valor switch
    {
        string texto when DateTime.TryParse(
            texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var data) => data,
        // Planilhas antigas gravavam com USER_ENTERED e o Sheets podia converter o texto em data (número serial).
        double serial when serial is > 0 and < 2958466 => DateTime.FromOADate(serial),
        _ => null
    };

    // ---------------------------------------------------------------------------------------------
    // Status (seção 10) e ações após o COMMIT (seção 17)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Seção 10: mantém o nome do arquivo e o Status da aba Controle coerentes com o status calculado.
    /// Só grava o que estiver diferente.
    /// </summary>
    public Task<PlanilhaVinculada> AtualizarStatusAsync(
        PlanilhaVinculada planilha, string status, CancellationToken ct = default) =>
        GravarStatusAsync(planilha, status, planilha.Controle.DataImportacao, ct);

    /// <summary>
    /// Chame SOMENTE depois que a transação SQL da importação tiver sido confirmada (COMMIT).
    /// Marca a aba Controle como importada, renomeia o arquivo e retira a permissão de edição.
    /// </summary>
    public async Task<PlanilhaVinculada> MarcarComoImportadaAsync(
        PlanilhaVinculada planilha, CancellationToken ct = default)
    {
        var importada = await GravarStatusAsync(
            planilha, NomesStatus.CotacaoImportada, planilha.Controle.DataImportacao ?? DateTime.Now, ct);

        // Ninguém mais deve conseguir editar depois que a cotação virou pedido no ETrade.
        await AlterarPermissaoPublicaAsync(planilha.SpreadsheetId, somenteLeitura: true, ct);
        return importada;
    }

    private async Task<PlanilhaVinculada> GravarStatusAsync(
        PlanilhaVinculada planilha, string status, DateTime? dataImportacao, CancellationToken ct)
    {
        var controle = planilha.Controle with { Status = status, DataImportacao = dataImportacao };
        var nome = NomeArquivoCotacao.Gerar(status, controle.Filial, controle.Sequencia);

        if (controle != planilha.Controle)
            await EscreverControleAsync(planilha.SpreadsheetId, controle, ct);

        if (!string.Equals(nome, planilha.Nome, StringComparison.Ordinal))
            await RenomearAsync(planilha.SpreadsheetId, nome, ct);

        return new PlanilhaVinculada(planilha.SpreadsheetId, nome, controle);
    }

    // ---------------------------------------------------------------------------------------------
    // Arquivar (seção 19.1) - usado pelo Sobrescrever, cuja regra fica na camada de Negócio
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Renomeia a planilha como histórico, move para "Registros Arquivados" e revoga o link público:
    /// ela deixa de ser a planilha ativa do pedido.
    /// </summary>
    public async Task ArquivarAsync(PlanilhaVinculada planilha, CancellationToken ct = default)
    {
        var carimbo = DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);
        var nomeHistorico = $"[Arquivado {carimbo}] {planilha.Nome}";

        await RenomearAsync(planilha.SpreadsheetId, nomeHistorico, ct);
        await MoverParaPastaAsync(
            planilha.SpreadsheetId, destinoId: _pastaArquivadosId, origemId: _pastaRaizId, metadados: null, ct);

        // A planilha arquivada é só para consulta/histórico (seção 19.1, item 4): ninguém de fora
        // deve conseguir abri-la ou editá-la mais.
        await RevogarPermissaoPublicaAsync(planilha.SpreadsheetId, ct);
    }

    // ---------------------------------------------------------------------------------------------
    // Histórico: planilhas ativas e arquivadas
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<RegistroHistorico>> ListarHistoricoAsync(CancellationToken ct = default)
    {
        var registros = new List<RegistroHistorico>();
        string? proximaPagina = null;

        do
        {
            var listar = _drive.Files.List();
            listar.Q = $"('{_pastaRaizId}' in parents or '{_pastaArquivadosId}' in parents) " +
                       $"and mimeType = '{LayoutPlanilha.MimeTypePlanilha}' and trashed = false";
            listar.Fields = "nextPageToken, files(id, name, parents, createdTime, modifiedTime, appProperties)";
            listar.Spaces = "drive";
            listar.OrderBy = "createdTime desc";
            listar.PageSize = 1000;
            listar.PageToken = proximaPagina;

            var resultado = await listar.ExecuteAsync(ct);
            foreach (var arquivo in resultado.Files ?? [])
                registros.Add(ParaRegistroHistorico(arquivo));

            proximaPagina = resultado.NextPageToken;
        } while (proximaPagina is not null);

        return registros;
    }

    private RegistroHistorico ParaRegistroHistorico(GoogleFile arquivo)
    {
        var nome = arquivo.Name ?? "";
        var partes = NomeDaPlanilha().Match(nome);

        // O vínculo gravado no arquivo vale mais que o nome; o nome cobre planilhas antigas.
        int? Numero(string propriedade, string grupo)
        {
            if (arquivo.AppProperties?.TryGetValue(propriedade, out var valor) == true &&
                int.TryParse(valor, out var doVinculo))
                return doVinculo;

            return partes.Success && int.TryParse(partes.Groups[grupo].Value, out var doNome) ? doNome : null;
        }

        return new RegistroHistorico(
            SpreadsheetId: arquivo.Id,
            Nome: nome,
            Status: partes.Success ? partes.Groups["status"].Value : "",
            Filial: Numero(LayoutPlanilha.PropriedadeFilial, "filial"),
            Sequencia: Numero(LayoutPlanilha.PropriedadeSequencia, "sequencia"),
            Arquivada: arquivo.Parents?.Contains(_pastaArquivadosId) == true,
            CriadaEm: arquivo.CreatedTimeDateTimeOffset?.LocalDateTime,
            AlteradaEm: arquivo.ModifiedTimeDateTimeOffset?.LocalDateTime);
    }

    /// <summary>"[Arquivado aaaa-mm-dd hhmm] Status - Filial N - Pedido N" (o prefixo só existe nas arquivadas).</summary>
    [GeneratedRegex(@"^(?:\[Arquivado [^\]]*\] )?(?<status>.+?) - Filial (?<filial>\d+) - Pedido (?<sequencia>\d+)$")]
    private static partial Regex NomeDaPlanilha();

    // ---------------------------------------------------------------------------------------------
    // Utilitários de Drive/Sheets
    // ---------------------------------------------------------------------------------------------

    private async Task RenomearAsync(string spreadsheetId, string novoNome, CancellationToken ct)
    {
        // Renomear o título da planilha também renomeia o arquivo no Drive (mesmo nome nos dois lugares).
        var lote = new BatchUpdateSpreadsheetRequest
        {
            Requests =
            [
                new Request
                {
                    UpdateSpreadsheetProperties = new UpdateSpreadsheetPropertiesRequest
                    {
                        Properties = new SpreadsheetProperties { Title = novoNome },
                        Fields = "title"
                    }
                }
            ]
        };
        await _sheets.Spreadsheets.BatchUpdate(lote, spreadsheetId).ExecuteAsync(ct);
    }

    private async Task MoverParaPastaAsync(
        string fileId, string destinoId, string origemId, GoogleFile? metadados, CancellationToken ct)
    {
        var atualizar = _drive.Files.Update(metadados ?? new GoogleFile(), fileId);
        atualizar.AddParents = destinoId;
        atualizar.RemoveParents = origemId;
        atualizar.Fields = "id, parents";
        await atualizar.ExecuteAsync(ct);
    }

    /// <summary>Compartilha o arquivo com "qualquer pessoa com o link" (fornecedor não precisa de conta Google).</summary>
    private async Task CompartilharComQualquerPessoaComLinkAsync(
        string spreadsheetId, bool somenteLeitura, CancellationToken ct)
    {
        var permissao = new Google.Apis.Drive.v3.Data.Permission
        {
            Type = "anyone",
            Role = somenteLeitura ? "reader" : "writer"
        };
        var criar = _drive.Permissions.Create(permissao, spreadsheetId);
        criar.Fields = "id";
        await criar.ExecuteAsync(ct);
    }

    private async Task AlterarPermissaoPublicaAsync(string spreadsheetId, bool somenteLeitura, CancellationToken ct)
    {
        var listar = _drive.Permissions.List(spreadsheetId);
        listar.Fields = "permissions(id, type, role)";
        var permissoes = await listar.ExecuteAsync(ct);

        var doPublico = permissoes.Permissions?.FirstOrDefault(p => p.Type == "anyone");
        if (doPublico is null)
            return;

        var atualizar = _drive.Permissions.Update(
            new Google.Apis.Drive.v3.Data.Permission { Role = somenteLeitura ? "reader" : "writer" },
            spreadsheetId,
            doPublico.Id);
        await atualizar.ExecuteAsync(ct);
    }

    private async Task RevogarPermissaoPublicaAsync(string spreadsheetId, CancellationToken ct)
    {
        var listar = _drive.Permissions.List(spreadsheetId);
        listar.Fields = "permissions(id, type)";
        var permissoes = await listar.ExecuteAsync(ct);

        var doPublico = permissoes.Permissions?.FirstOrDefault(p => p.Type == "anyone");
        if (doPublico is null)
            return;

        await _drive.Permissions.Delete(spreadsheetId, doPublico.Id).ExecuteAsync(ct);
    }

    private async Task TentarExcluirAsync(string spreadsheetId)
    {
        try { await _drive.Files.Delete(spreadsheetId).ExecuteAsync(); }
        catch { /* melhor deixar um arquivo órfão do que mascarar o erro original */ }
    }

    public void Dispose()
    {
        _sheets.Dispose();
        _drive.Dispose();
    }
}
