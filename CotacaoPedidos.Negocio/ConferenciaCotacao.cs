namespace CotacaoPedidos.Negocio;

/// <summary>
/// Conferências feitas antes de iniciar a transação de importação (seção 13). Funções puras: recebem o que
/// foi lido do ETrade e da planilha e devolvem as mensagens de bloqueio, sem acessar banco nem Google.
/// </summary>
public static class ConferenciaCotacao
{
    private const int MaximoMensagens = 10;

    /// <summary>
    /// Compara as colunas que não são Preço (Código, Código EAN, Nome e Quantidade) com os itens atuais do
    /// pedido. Qualquer diferença significa que a planilha foi alterada fora da coluna Preço ou que o pedido
    /// mudou no ETrade depois da exportação - nos dois casos a importação não pode continuar.
    /// </summary>
    public static IReadOnlyList<string> CompararItens(
        IReadOnlyList<ItemPedido> doPedido, IReadOnlyList<LinhaCotacao> daPlanilha)
    {
        var divergencias = new List<string>();

        if (doPedido.Count != daPlanilha.Count)
            divergencias.Add($"O pedido tem {doPedido.Count} itens e a planilha tem {daPlanilha.Count}.");

        // O mesmo produto pode aparecer em mais de uma linha do pedido, por isso a comparação é por grupo.
        var pedidoPorCodigo = doPedido.ToLookup(i => i.Codigo.Trim(), StringComparer.OrdinalIgnoreCase);
        var planilhaPorCodigo = daPlanilha.ToLookup(l => l.Codigo.Trim(), StringComparer.OrdinalIgnoreCase);

        var codigos = pedidoPorCodigo.Select(g => g.Key)
            .Union(planilhaPorCodigo.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var codigo in codigos)
        {
            var itens = pedidoPorCodigo[codigo].OrderBy(i => i.Quantidade).ThenBy(i => i.Nome).ToList();
            var linhas = planilhaPorCodigo[codigo].OrderBy(l => l.Quantidade).ThenBy(l => l.Nome).ToList();

            if (itens.Count == 0)
            {
                foreach (var linha in linhas)
                    divergencias.Add($"Linha {linha.NumeroLinha}: o produto '{codigo}' não pertence ao pedido.");
                continue;
            }

            if (linhas.Count == 0)
            {
                divergencias.Add($"O produto {codigo} ({itens[0].Nome.Trim()}) está no pedido, mas não na planilha.");
                continue;
            }

            if (itens.Count != linhas.Count)
            {
                divergencias.Add(
                    $"O produto {codigo} aparece {itens.Count}x no pedido e {linhas.Count}x na planilha.");
                continue;
            }

            for (var i = 0; i < itens.Count; i++)
                CompararLinha(itens[i], linhas[i], divergencias);
        }

        return Resumir(divergencias);
    }

    private static void CompararLinha(ItemPedido item, LinhaCotacao linha, List<string> divergencias)
    {
        var prefixo = $"Linha {linha.NumeroLinha} (produto {item.Codigo.Trim()})";

        if (!TextoIgual(item.CodigoEan, linha.CodigoEan))
            divergencias.Add($"{prefixo}: Código EAN '{linha.CodigoEan}' na planilha, '{item.CodigoEan.Trim()}' no ETrade.");

        if (!TextoIgual(item.Nome, linha.Nome))
            divergencias.Add($"{prefixo}: Nome '{linha.Nome}' na planilha, '{item.Nome.Trim()}' no ETrade.");

        // Movimento_Produto.Qtde tem 5 casas decimais.
        if (linha.Quantidade is not { } quantidade || decimal.Round(quantidade, 5) != decimal.Round(item.Quantidade, 5))
            divergencias.Add(
                $"{prefixo}: Quantidade {linha.Quantidade?.ToString() ?? "(vazia)"} na planilha, " +
                $"{item.Quantidade:0.#####} no ETrade.");
    }

    /// <summary>
    /// Seções 13 e 21: preço que não é número, negativo, com mais de 4 casas, ou o mesmo produto com preços
    /// diferentes em linhas diferentes (o UPDATE grava um preço por código) bloqueia a importação.
    /// </summary>
    public static IReadOnlyList<string> ValidarPrecos(IReadOnlyList<LinhaCotacao> linhas)
    {
        var erros = new List<string>();

        foreach (var linha in linhas)
        {
            if (linha.PrecoInvalido)
                erros.Add($"Linha {linha.NumeroLinha} (produto {linha.Codigo}): '{linha.PrecoDigitado}' não é um preço válido.");
            else if (linha.Preco < 0)
                erros.Add($"Linha {linha.NumeroLinha} (produto {linha.Codigo}): preço negativo.");
            // A coluna de destino guarda 4 casas; mais que isso seria arredondado em silêncio.
            else if (linha.Preco is { } preco && decimal.Round(preco, 4) != preco)
                erros.Add($"Linha {linha.NumeroLinha} (produto {linha.Codigo}): preço com mais de 4 casas decimais.");
        }

        foreach (var grupo in linhas.GroupBy(l => l.Codigo.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            if (grupo.Select(l => l.Preco ?? 0m).Distinct().Count() > 1)
                erros.Add(
                    $"O produto {grupo.Key} aparece nas linhas {string.Join(", ", grupo.Select(l => l.NumeroLinha))} " +
                    "com preços diferentes.");
        }

        return Resumir(erros);
    }

    private static bool TextoIgual(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);

    private static IReadOnlyList<string> Resumir(List<string> mensagens)
    {
        if (mensagens.Count <= MaximoMensagens)
            return mensagens;

        var resumo = mensagens.Take(MaximoMensagens).ToList();
        resumo.Add($"... e mais {mensagens.Count - MaximoMensagens} divergência(s).");
        return resumo;
    }
}

/// <summary>Resumo exibido ao comprador antes da confirmação definitiva da importação (seção 14).</summary>
public sealed record ResumoImportacao(
    PedidoMovimento Pedido,
    PlanilhaVinculada Planilha,
    IReadOnlyList<LinhaCotacao> Linhas)
{
    public int TotalProdutos => Linhas.Count;
    public int ComPreco => Linhas.Count(l => l.PrecoPreenchido);
    public int SemPreco => Linhas.Count(l => !l.PrecoPreenchido);

    /// <summary>Vazios (convertidos para 0,00) mais os que tiveram 0 digitado.</summary>
    public int Zerados => Linhas.Count(l => (l.Preco ?? 0m) == 0m);

    /// <summary>Mesma conta do UPDATE: Valor_Total = Qtde × Novo_Preço (5 casas), somado em 2 casas.</summary>
    public decimal ValorTotal =>
        decimal.Round(Linhas.Sum(l => decimal.Round((l.Quantidade ?? 0m) * (l.Preco ?? 0m), 5)), 2);

    /// <summary>
    /// Seção 15: todo item participa; célula vazia vira 0,00 somente aqui. Um preço por código - linhas
    /// repetidas do mesmo produto já foram conferidas como iguais em <see cref="ConferenciaCotacao.ValidarPrecos"/>.
    /// </summary>
    public IReadOnlyList<PrecoItem> Precos =>
        Linhas.GroupBy(l => l.Codigo.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new PrecoItem(g.Key, g.First().Preco ?? 0m))
            .ToList();
}
