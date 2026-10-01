namespace CotacaoPedidos.Negocio;

public static class ETradeConstantes
{
    /// <summary>Operação 70 - Pedido de Compra.</summary>
    public const int OperacaoPedidoCompra = 70;

    // O Operacao__Ide da Operação 72 (seção 16.3 da especificação) NÃO fica aqui: é diferente em cada
    // instalação do ETrade. Vem da configuração (appsettings.json) e é conferido contra a tabela Operacao
    // na inicialização - veja ETradeRepository.ValidarConfiguracaoAsync.

    /// <summary>Operação 72 - Cotação Realizada.</summary>
    public const int OperacaoCotacaoRealizada = 72;
}
