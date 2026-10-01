using System.Data;
using CotacaoPedidos.Negocio;
using Microsoft.Data.SqlClient;

namespace CotacaoPedidos.Dados;

/// <summary>
/// Acesso ao banco ETrade (SQL Server). Todas as consultas usam parâmetros.
/// A importação de preços roda em UMA transação: qualquer falha => ROLLBACK e o pedido continua na Operação 70.
/// </summary>
public sealed class ETradeRepository : IETradeRepository
{
    private readonly string _connectionString;
    private readonly Guid _operacaoCotacaoRealizadaIde;

    /// <param name="operacaoCotacaoRealizadaIde">
    /// Operacao__Ide da Operação 72 neste banco ETrade (gravado no movimento ao importar).
    /// </param>
    public ETradeRepository(string connectionString, Guid operacaoCotacaoRealizadaIde)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (operacaoCotacaoRealizadaIde == Guid.Empty)
            throw new ArgumentException("Operacao__Ide da Operação 72 não informado.", nameof(operacaoCotacaoRealizadaIde));

        _connectionString = connectionString;
        _operacaoCotacaoRealizadaIde = operacaoCotacaoRealizadaIde;
    }

    /// <summary>
    /// Confere, antes de qualquer uso, que o Operacao__Ide configurado é mesmo o da Operação 72 deste banco.
    /// Um GUID errado seria gravado nos movimentos importados e deixaria o ETrade com dados inconsistentes.
    /// </summary>
    public async Task ValidarConfiguracaoAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT COUNT(*) FROM Operacao WHERE Ide = @Ide AND Codigo = @Codigo;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Ide", SqlDbType.UniqueIdentifier).Value = _operacaoCotacaoRealizadaIde;
        cmd.Parameters.Add("@Codigo", SqlDbType.Int).Value = ETradeConstantes.OperacaoCotacaoRealizada;

        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) != 1)
            throw new InvalidOperationException(
                $"O Operacao__Ide configurado ({_operacaoCotacaoRealizadaIde}) não corresponde à Operação " +
                $"{ETradeConstantes.OperacaoCotacaoRealizada} na tabela Operacao do ETrade. " +
                "Corrija \"OperacaoCotacaoRealizadaIde\" no appsettings.json.");
    }

    // ---------------------------------------------------------------------------------------------
    // Localizar pedido (botão "Localizar pedido")
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Busca o movimento ativo por Filial + Sequência nas operações 70 e 72.
    /// Retorna null se não existir (=> "Pedido não localizado").
    /// </summary>
    public async Task<PedidoMovimento?> LocalizarPedidoAsync(
        int filial, int sequencia, CancellationToken ct = default)
    {
        // TOP (2) e não TOP (1): o programa inteiro depende de Filial + Sequência ser único. Se houver dois
        // movimentos, escolher um "ao acaso" poderia exportar/importar o pedido errado.
        const string sql = """
            SELECT TOP (2)
                m.Ide, m.Filial__Codigo, m.Sequencia, m.Operacao__Codigo, m.Operacao__Ide,
                m.Desefetivado, m.Total_Produtos, m.Total_Final
            FROM Movimento m
            WHERE m.Filial__Codigo = @Filial
              AND m.Sequencia = @Sequencia
              AND m.Status <> -1
              AND m.Operacao__Codigo IN (@Op70, @Op72);
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Filial", SqlDbType.Int).Value = filial;
        cmd.Parameters.Add("@Sequencia", SqlDbType.Int).Value = sequencia;
        cmd.Parameters.Add("@Op70", SqlDbType.Int).Value = ETradeConstantes.OperacaoPedidoCompra;
        cmd.Parameters.Add("@Op72", SqlDbType.Int).Value = ETradeConstantes.OperacaoCotacaoRealizada;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var pedido = new PedidoMovimento(
            Ide: reader.GetGuid(0),
            Filial: reader.GetInt32(1),
            Sequencia: reader.GetInt32(2),
            Operacao: reader.GetInt32(3),
            OperacaoIde: reader.GetGuid(4),
            Desefetivado: reader.GetBoolean(5),
            TotalProdutos: reader.GetDecimal(6),
            TotalFinal: reader.GetDecimal(7));

        if (await reader.ReadAsync(ct))
            throw new OperacaoBloqueadaException(
                $"Existe mais de um movimento ativo para Filial {filial} - Sequência {sequencia} nas operações " +
                "70/72. Corrija no ETrade antes de continuar.");

        return pedido;
    }

    // ---------------------------------------------------------------------------------------------
    // Lupa da Filial
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<Filial>> ListarFiliaisAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT f.Codigo, f.Nome, f.Cidade, f.UF
            FROM Filial f
            WHERE f.Status <> -1
              AND f.Inativo = 0
            ORDER BY f.Codigo;
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var filiais = new List<Filial>();
        while (await reader.ReadAsync(ct))
            filiais.Add(new Filial(reader.GetInt32(0), reader.GetString(1).Trim(), reader.GetString(2).Trim(), reader.GetString(3)));

        return filiais;
    }

    // ---------------------------------------------------------------------------------------------
    // Itens para exportar (aba Cotação)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Itens do pedido na Operação 70, ordenados por nome (seção 9 da especificação).</summary>
    public async Task<IReadOnlyList<ItemPedido>> ObterItensParaCotacaoAsync(
        PedidoMovimento pedido, CancellationToken ct = default)
    {
        const string sql = """
            SELECT p.Codigo, p.Codigo_EAN, p.Nome, mp.Qtde
            FROM Movimento_Produto mp
            INNER JOIN Produto p ON p.Ide = mp.Produto__Ide
            INNER JOIN Movimento m ON m.Ide = mp.Movimento__Ide
            WHERE m.Ide = @MovimentoIde
              AND m.Filial__Codigo = @Filial
              AND m.Sequencia = @Sequencia
              AND m.Status <> -1
              AND m.Operacao__Codigo = @Op70
              AND mp.Status <> -1
              AND p.Status <> -1
              AND mp.Tipo <> 'C'
            ORDER BY p.Nome;
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = CriarComando(conn, null, sql, pedido);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var itens = new List<ItemPedido>();
        while (await reader.ReadAsync(ct))
        {
            itens.Add(new ItemPedido(
                Codigo: reader.GetString(0),
                CodigoEan: reader.GetString(1),
                Nome: reader.GetString(2),
                Quantidade: reader.GetDecimal(3)));
        }

        return itens;
    }

    // ---------------------------------------------------------------------------------------------
    // Importação (botão "Importar")
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Grava os preços nos itens, recalcula os totais do movimento e muda a operação 70 -> 72,
    /// tudo na mesma transação. Os preços chegam já validados pela camada de Negócio (um por código).
    /// As travas abaixo protegem contra mudanças no ETrade entre a validação e a transação;
    /// qualquer exceção desfaz tudo (ROLLBACK).
    /// </summary>
    public async Task<ResultadoImportacao> ImportarPrecosAsync(
        PedidoMovimento pedido, IReadOnlyCollection<PrecoItem> precos, CancellationToken ct = default)
    {
        if (precos.Count == 0)
            throw new ArgumentException("Nenhum preço foi informado para importação.", nameof(precos));

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Qualquer erro de execução aborta a transação inteira.
        await using (var xact = new SqlCommand("SET XACT_ABORT ON;", conn))
            await xact.ExecuteNonQueryAsync(ct);

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await BloquearEValidarMovimentoAsync(conn, tx, pedido, ct);
            await CarregarPrecosTemporariosAsync(conn, tx, precos, ct);

            var itensEsperados = await ContarItensElegiveisAsync(conn, tx, pedido, ct);
            var itensAtualizados = await AtualizarItensAsync(conn, tx, pedido, ct);

            // "Todos os produtos foram processados" (regra de segurança da especificação).
            if (itensAtualizados != itensEsperados)
                throw new OperacaoBloqueadaException(
                    $"Nem todos os itens do pedido foram encontrados na planilha " +
                    $"({itensAtualizados} de {itensEsperados}). Nenhuma alteração foi gravada.");

            var resultado = await AtualizarMovimentoAsync(
                conn, tx, pedido, itensAtualizados, _operacaoCotacaoRealizadaIde, ct);

            await tx.CommitAsync(ct);
            return resultado;
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch { /* conexão já perdida: o SQL Server desfaz a transação sozinho */ }
            throw;
        }
    }

    /// <summary>
    /// Trava a linha do movimento e confirma que ele continua ativo, não cancelado e na Operação 70
    /// (o pedido pode ter mudado no ETrade desde que a tela foi aberta).
    /// </summary>
    private static async Task BloquearEValidarMovimentoAsync(
        SqlConnection conn, SqlTransaction tx, PedidoMovimento pedido, CancellationToken ct)
    {
        const string sql = """
            SELECT m.Operacao__Codigo, m.Status, m.Desefetivado
            FROM Movimento m WITH (UPDLOCK, ROWLOCK)
            WHERE m.Ide = @MovimentoIde
              AND m.Filial__Codigo = @Filial
              AND m.Sequencia = @Sequencia;
            """;

        await using var cmd = CriarComando(conn, tx, sql, pedido);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            throw new OperacaoBloqueadaException("O pedido não existe mais no ETrade.");

        var operacao = reader.GetInt32(0);
        var status = reader.GetInt32(1);
        var desefetivado = reader.GetBoolean(2);

        if (status == -1)
            throw new OperacaoBloqueadaException("O pedido não está mais ativo no ETrade.");

        if (desefetivado)
            throw new OperacaoBloqueadaException("O pedido foi cancelado no ETrade.");

        if (operacao != ETradeConstantes.OperacaoPedidoCompra)
            throw new OperacaoBloqueadaException(
                "O pedido não está mais na Operação 70 - Pedido de Compra.");
    }

    /// <summary>
    /// Cria #Precos na própria transação. COLLATE DATABASE_DEFAULT evita erro de collation
    /// entre o tempdb e o banco ETrade no JOIN com Produto.Codigo.
    /// </summary>
    private static async Task CarregarPrecosTemporariosAsync(
        SqlConnection conn, SqlTransaction tx, IReadOnlyCollection<PrecoItem> precos, CancellationToken ct)
    {
        const string criar = """
            CREATE TABLE #Precos (
                Codigo     VARCHAR(30) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,
                Novo_Preco DECIMAL(18,4) NOT NULL
            );
            """;

        await using (var cmd = new SqlCommand(criar, conn, tx))
            await cmd.ExecuteNonQueryAsync(ct);

        var tabela = new DataTable();
        tabela.Columns.Add("Codigo", typeof(string));
        tabela.Columns.Add("Novo_Preco", typeof(decimal));
        foreach (var p in precos)
            tabela.Rows.Add(p.Codigo, p.NovoPreco);

        using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx)
        {
            DestinationTableName = "#Precos"
        };
        bulk.ColumnMappings.Add("Codigo", "Codigo");
        bulk.ColumnMappings.Add("Novo_Preco", "Novo_Preco");
        await bulk.WriteToServerAsync(tabela, ct);
    }

    private static async Task<int> ContarItensElegiveisAsync(
        SqlConnection conn, SqlTransaction tx, PedidoMovimento pedido, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM Movimento_Produto mp
            INNER JOIN Produto p ON p.Ide = mp.Produto__Ide
            INNER JOIN Movimento m ON m.Ide = mp.Movimento__Ide
            WHERE m.Ide = @MovimentoIde
              AND m.Filial__Codigo = @Filial
              AND m.Sequencia = @Sequencia
              AND m.Status <> -1
              AND m.Operacao__Codigo = @Op70
              AND mp.Status <> -1
              AND p.Status <> -1
              AND mp.Tipo <> 'C';
            """;

        await using var cmd = CriarComando(conn, tx, sql, pedido);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Passo 1 da seção 15: grava Valor_Unit, Valor_Total e Valor_Final dos itens.
    /// Usa SELECT @@ROWCOUNT (e não o retorno do ExecuteNonQuery) para não contar linhas de triggers.
    /// </summary>
    private static async Task<int> AtualizarItensAsync(
        SqlConnection conn, SqlTransaction tx, PedidoMovimento pedido, CancellationToken ct)
    {
        const string sql = """
            UPDATE mp
            SET mp.Valor_Unit  = pr.Novo_Preco,
                mp.Valor_Total = mp.Qtde * pr.Novo_Preco,
                mp.Valor_Final = mp.Qtde * pr.Novo_Preco
            FROM Movimento_Produto mp
            INNER JOIN Produto p ON p.Ide = mp.Produto__Ide
            INNER JOIN Movimento m ON m.Ide = mp.Movimento__Ide
            INNER JOIN #Precos pr ON pr.Codigo = p.Codigo
            WHERE m.Ide = @MovimentoIde
              AND m.Filial__Codigo = @Filial
              AND m.Sequencia = @Sequencia
              AND m.Status <> -1
              AND m.Operacao__Codigo = @Op70
              AND mp.Status <> -1
              AND p.Status <> -1
              AND mp.Tipo <> 'C';

            SELECT @@ROWCOUNT;
            """;

        await using var cmd = CriarComando(conn, tx, sql, pedido);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Passo 2 da seção 15: recalcula os totais e muda a operação 70 -> 72.
    /// Exatamente 1 movimento deve ser alterado; senão lança exceção e a transação é desfeita.
    /// </summary>
    private static async Task<ResultadoImportacao> AtualizarMovimentoAsync(
        SqlConnection conn, SqlTransaction tx, PedidoMovimento pedido, int itensAtualizados,
        Guid operacaoCotacaoRealizadaIde, CancellationToken ct)
    {
        // Seção 16.2: Total_Final soma Valor_Final (não Valor_Total) - itens fora da cotação podem ter
        // Valor_Final diferente de Valor_Total.
        const string atualizar = """
            UPDATE m
            SET m.Total_Produtos  = ISNULL(t.Total_Produtos, 0),
                m.Total_Final     = ISNULL(t.Total_Final, 0),
                m.Operacao__Codigo = @Op72,
                m.Operacao__Ide    = @Op72Ide,
                m.Data_Alteracao   = GETDATE()
            FROM Movimento m
            CROSS APPLY (
                SELECT SUM(mp.Valor_Total) AS Total_Produtos,
                       SUM(mp.Valor_Final) AS Total_Final
                FROM Movimento_Produto mp
                WHERE mp.Movimento__Ide = m.Ide
                  AND mp.Status <> -1
                  AND mp.Tipo <> 'C'
            ) t
            WHERE m.Ide = @MovimentoIde
              AND m.Filial__Codigo = @Filial
              AND m.Sequencia = @Sequencia
              AND m.Status <> -1
              AND m.Operacao__Codigo = @Op70;

            SELECT @@ROWCOUNT;
            """;

        await using (var cmd = CriarComando(conn, tx, atualizar, pedido))
        {
            cmd.Parameters.Add("@Op72Ide", SqlDbType.UniqueIdentifier).Value = operacaoCotacaoRealizadaIde;
            var linhas = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            if (linhas != 1)
                throw new InvalidOperationException(
                    $"A atualização do movimento alterou {linhas} registros (esperado: 1).");
        }

        const string ler = "SELECT Total_Produtos, Total_Final FROM Movimento WHERE Ide = @MovimentoIde;";

        await using var cmdLer = CriarComando(conn, tx, ler, pedido);
        await using var reader = await cmdLer.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        return new ResultadoImportacao(itensAtualizados, reader.GetDecimal(0), reader.GetDecimal(1));
    }

    /// <summary>
    /// Cria o comando já com os parâmetros comuns do pedido. Parâmetros que uma consulta específica
    /// não usa são simplesmente ignorados pelo SQL Server.
    /// </summary>
    private static SqlCommand CriarComando(
        SqlConnection conn, SqlTransaction? tx, string sql, PedidoMovimento pedido)
    {
        var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@MovimentoIde", SqlDbType.UniqueIdentifier).Value = pedido.Ide;
        cmd.Parameters.Add("@Filial", SqlDbType.Int).Value = pedido.Filial;
        cmd.Parameters.Add("@Sequencia", SqlDbType.Int).Value = pedido.Sequencia;
        cmd.Parameters.Add("@Op70", SqlDbType.Int).Value = ETradeConstantes.OperacaoPedidoCompra;
        cmd.Parameters.Add("@Op72", SqlDbType.Int).Value = ETradeConstantes.OperacaoCotacaoRealizada;
        return cmd;
    }
}
