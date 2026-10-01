namespace CotacaoPedidos.Negocio;

/// <summary>
/// Fluxos da tela de cotação: localizar/exportar o pedido para a planilha, sobrescrever e importar os preços
/// de volta para o ETrade. Toda ação começa consultando de novo o banco e a planilha: o que está na tela pode
/// estar desatualizado (o pedido pode ter mudado no ETrade, o fornecedor pode ter mexido na planilha).
/// </summary>
public sealed class CotacaoService
{
    private readonly IETradeRepository _etrade;
    private readonly IPlanilhaRepository _planilhas;

    public CotacaoService(IETradeRepository etrade, IPlanilhaRepository planilhas)
    {
        _etrade = etrade ?? throw new ArgumentNullException(nameof(etrade));
        _planilhas = planilhas ?? throw new ArgumentNullException(nameof(planilhas));
    }

    public Task<IReadOnlyList<Filial>> ListarFiliaisAsync(CancellationToken ct = default) =>
        _etrade.ListarFiliaisAsync(ct);

    public Task<IReadOnlyList<RegistroHistorico>> ListarHistoricoAsync(CancellationToken ct = default) =>
        _planilhas.ListarHistoricoAsync(ct);

    // ---------------------------------------------------------------------------------------------
    // Localizar pedido (seção 7)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Localiza o pedido no ETrade, lê a planilha vinculada (se houver), calcula o status e confere os itens.
    /// Também mantém o nome do arquivo coerente com o status (seção 10).
    /// </summary>
    public async Task<SituacaoCotacao> LocalizarPedidoAsync(
        int filial, int sequencia, CancellationToken ct = default)
    {
        var situacao = await ConsultarSituacaoAsync(filial, sequencia, ct);
        return await ManterPlanilhaCoerenteAsync(situacao, ct);
    }

    private async Task<SituacaoCotacao> ConsultarSituacaoAsync(int filial, int sequencia, CancellationToken ct)
    {
        var pedido = await _etrade.LocalizarPedidoAsync(filial, sequencia, ct);
        if (pedido is null)
            return new SituacaoCotacao(filial, sequencia, StatusCotacao.PedidoNaoLocalizado, null, null, [], []);

        var planilha = await _planilhas.LocalizarPlanilhaAtivaAsync(pedido, ct);
        if (planilha is null)
            return new SituacaoCotacao(filial, sequencia, CalculadoraStatus.Calcular(pedido, null), pedido, null, [], []);

        var linhas = await _planilhas.LerLinhasAsync(planilha.SpreadsheetId, ct);
        var estado = new EstadoPlanilha(
            TemAlgumPrecoPreenchido: linhas.Any(l => l.PrecoPreenchido),
            ControleIndicaImportada: EstaImportada(planilha));

        // A conferência dos itens só interessa enquanto a cotação ainda pode ser importada.
        IReadOnlyList<string> divergencias = [];
        if (pedido is { EstaNaOperacao70: true, Desefetivado: false })
        {
            var itens = await _etrade.ObterItensParaCotacaoAsync(pedido, ct);
            divergencias = ConferenciaCotacao.CompararItens(itens, linhas);
        }

        return new SituacaoCotacao(
            filial, sequencia, CalculadoraStatus.Calcular(pedido, estado), pedido, planilha, linhas, divergencias);
    }

    private async Task<SituacaoCotacao> ManterPlanilhaCoerenteAsync(SituacaoCotacao situacao, CancellationToken ct)
    {
        if (situacao is not { Pedido: { } pedido, Planilha: { } planilha })
            return situacao;

        // Uma planilha marcada como importada nunca volta atrás: a marca é a trava contra importação
        // duplicada (seção 19.2), mesmo que alguém devolva o pedido para a Operação 70 no ETrade.
        if (EstaImportada(planilha))
            return situacao;

        // COMMIT feito, mas a planilha não chegou a ser marcada (ex.: queda de internet logo após importar):
        // conclui a seção 17 agora, para a consulta voltar a mostrar Cotação Importada.
        if (pedido.EstaNaOperacao72 && !pedido.Desefetivado && planilha.Controle.MovimentoIde == pedido.Ide)
        {
            var importada = await _planilhas.MarcarComoImportadaAsync(planilha, ct);
            return situacao with { Status = StatusCotacao.CotacaoImportada, Planilha = importada };
        }

        // Fornecedor preencheu (ou apagou) preços desde a última consulta: renomeia o arquivo.
        var statusDoArquivo = situacao.Status switch
        {
            StatusCotacao.AguardandoCotacao => NomesStatus.AguardandoCotacao,
            StatusCotacao.CotacaoRealizada => NomesStatus.CotacaoRealizada,
            _ => null
        };
        if (statusDoArquivo is null)
            return situacao;

        var atualizada = await _planilhas.AtualizarStatusAsync(planilha, statusDoArquivo, ct);
        return situacao with { Planilha = atualizada };
    }

    // ---------------------------------------------------------------------------------------------
    // Exportar (seção 8) e Sobrescrever (seção 19.1)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Cria a planilha de cotação com os itens do pedido. Se já existir planilha ativa, não cria outra:
    /// devolve a existente com <see cref="ResultadoExportacao.JaExistia"/> = true, para a tela oferecer
    /// a sobrescrita.
    /// </summary>
    public async Task<ResultadoExportacao> ExportarAsync(
        int filial, int sequencia, CancellationToken ct = default)
    {
        var pedido = await LocalizarPedidoNaOperacao70Async(filial, sequencia, ct);

        var planilhaExistente = await _planilhas.LocalizarPlanilhaAtivaAsync(pedido, ct);
        if (planilhaExistente is not null)
            return new ResultadoExportacao(planilhaExistente, JaExistia: true);

        var itens = await ObterItensAsync(pedido, ct);
        var planilha = await _planilhas.CriarPlanilhaAsync(pedido, itens, ct);
        return new ResultadoExportacao(planilha, JaExistia: false);
    }

    /// <summary>
    /// Arquiva a planilha ativa e cria uma nova com os itens atuais do pedido (seção 19.1). Só com o pedido
    /// na Operação 70 e a cotação atual ainda não importada.
    /// </summary>
    public async Task<PlanilhaVinculada> SobrescreverAsync(
        int filial, int sequencia, CancellationToken ct = default)
    {
        var pedido = await LocalizarPedidoNaOperacao70Async(filial, sequencia, ct);

        var planilhaAtual = await _planilhas.LocalizarPlanilhaAtivaAsync(pedido, ct)
            ?? throw new OperacaoBloqueadaException(
                "Não há planilha ativa para sobrescrever. Use Exportar para criar a primeira.");

        if (EstaImportada(planilhaAtual))
            throw new OperacaoBloqueadaException("Esta cotação já foi importada e não pode ser sobrescrita.");

        // Consulta os itens antes de arquivar: se o pedido ficou sem itens, nada é alterado.
        var itensAtuais = await ObterItensAsync(pedido, ct);

        await _planilhas.ArquivarAsync(planilhaAtual, ct);
        return await _planilhas.CriarPlanilhaAsync(pedido, itensAtuais, ct);
    }

    private async Task<IReadOnlyList<ItemPedido>> ObterItensAsync(PedidoMovimento pedido, CancellationToken ct)
    {
        var itens = await _etrade.ObterItensParaCotacaoAsync(pedido, ct);
        if (itens.Count == 0)
            throw new OperacaoBloqueadaException("O pedido não possui itens para cotação.");
        return itens;
    }

    // ---------------------------------------------------------------------------------------------
    // Importar (seções 13 a 17)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Faz todas as validações da seção 13 (sem alterar nada) e monta o resumo da seção 14 para o comprador
    /// confirmar. Lança <see cref="OperacaoBloqueadaException"/> se a importação não puder seguir.
    /// </summary>
    public async Task<ResumoImportacao> PrepararImportacaoAsync(
        int filial, int sequencia, CancellationToken ct = default)
    {
        var situacao = await ConsultarSituacaoAsync(filial, sequencia, ct);
        var pedido = ExigirPedidoNaOperacao70(situacao.Pedido, filial, sequencia);

        var planilha = situacao.Planilha
            ?? throw new OperacaoBloqueadaException(
                "Nenhuma planilha ativa foi encontrada para este pedido. Exporte antes de importar.");

        // Seções 13 e 18: nunca importar sem confirmar que a planilha pertence a este pedido.
        ValidacaoVinculo.Validar(pedido, planilha.Controle, planilha.SpreadsheetId);

        // Seção 5.3: Importar só com status Cotação Realizada (ao menos um preço preenchido).
        if (situacao.Status != StatusCotacao.CotacaoRealizada)
            throw new OperacaoBloqueadaException(
                $"A importação só é permitida com o status {NomesStatus.CotacaoRealizada}. " +
                $"Status atual: {situacao.Status.ParaTexto()} (nenhum preço foi preenchido na planilha).");

        if (situacao.Divergencias.Count > 0)
            throw new OperacaoBloqueadaException(
                "Os itens da planilha não conferem com o pedido no ETrade (o pedido foi alterado depois da " +
                "exportação ou a planilha foi modificada fora da coluna Preço). Use Sobrescrever para gerar " +
                "uma nova cotação.\n\n" + string.Join("\n", situacao.Divergencias));

        var errosDePreco = ConferenciaCotacao.ValidarPrecos(situacao.Linhas);
        if (errosDePreco.Count > 0)
            throw new OperacaoBloqueadaException(
                "Há preços inválidos na planilha. Corrija antes de importar.\n\n" + string.Join("\n", errosDePreco));

        return new ResumoImportacao(pedido, planilha, situacao.Linhas);
    }

    /// <summary>
    /// Importa o que o comprador conferiu. Revalida tudo antes da transação: se o pedido ou a planilha
    /// mudaram enquanto o resumo estava aberto, bloqueia em vez de importar valores que ninguém conferiu.
    /// Só marca a planilha como importada após o COMMIT.
    /// </summary>
    public async Task<ResultadoImportacao> ImportarAsync(ResumoImportacao conferido, CancellationToken ct = default)
    {
        var atual = await PrepararImportacaoAsync(conferido.Pedido.Filial, conferido.Pedido.Sequencia, ct);

        if (atual.Pedido.Ide != conferido.Pedido.Ide ||
            atual.Planilha.SpreadsheetId != conferido.Planilha.SpreadsheetId ||
            !atual.Linhas.SequenceEqual(conferido.Linhas))
            throw new OperacaoBloqueadaException(
                "A planilha foi alterada durante a conferência. Clique em Importar novamente para revisar o resumo.");

        // Uma transação para tudo: qualquer falha faz ROLLBACK e o pedido continua na Operação 70.
        var resultado = await _etrade.ImportarPrecosAsync(atual.Pedido, atual.Precos, ct);

        // Seção 17: somente depois do COMMIT a planilha é marcada como importada. O COMMIT já aconteceu,
        // então uma falha aqui não pode ser tratada como erro da importação.
        try
        {
            await _planilhas.MarcarComoImportadaAsync(atual.Planilha, CancellationToken.None);
            return resultado;
        }
        catch (Exception ex)
        {
            return resultado with
            {
                AvisoPlanilha =
                    $"Os preços foram gravados no ETrade, mas não foi possível atualizar a planilha ({ex.Message}). " +
                    "Clique em Localizar pedido para concluir essa etapa."
            };
        }
    }

    // ---------------------------------------------------------------------------------------------

    private static bool EstaImportada(PlanilhaVinculada planilha) =>
        string.Equals(planilha.Controle.Status, NomesStatus.CotacaoImportada, StringComparison.Ordinal);

    private async Task<PedidoMovimento> LocalizarPedidoNaOperacao70Async(
        int filial, int sequencia, CancellationToken ct)
    {
        var pedido = await _etrade.LocalizarPedidoAsync(filial, sequencia, ct);
        return ExigirPedidoNaOperacao70(pedido, filial, sequencia);
    }

    private static PedidoMovimento ExigirPedidoNaOperacao70(PedidoMovimento? pedido, int filial, int sequencia)
    {
        if (pedido is null)
            throw new OperacaoBloqueadaException(
                $"Pedido não localizado (Filial {filial}, Sequência {sequencia}).");

        if (pedido.Desefetivado)
            throw new OperacaoBloqueadaException("O pedido está cancelado (desefetivado).");

        if (!pedido.EstaNaOperacao70)
            throw new OperacaoBloqueadaException(
                "O pedido não está mais na Operação 70 - Pedido de Compra.");

        return pedido;
    }
}
