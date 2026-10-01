namespace CotacaoPedidos.Negocio;

/// <summary>
/// A operação (exportar, sobrescrever ou importar) foi bloqueada por uma regra de negócio, antes de qualquer
/// alteração permanecer no banco ou no Drive. A mensagem pode ser exibida ao comprador.
/// </summary>
public sealed class OperacaoBloqueadaException : Exception
{
    public OperacaoBloqueadaException(string message) : base(message) { }
}

/// <summary>
/// Uma planilha foi encontrada, mas a aba Controle não confere com o pedido esperado (seção 4: a planilha
/// nunca deve ser identificada só pelo nome). Indica dado corrompido ou duas planilhas concorrentes para o
/// mesmo pedido - situação que não deveria ocorrer em uso normal.
/// </summary>
public sealed class PlanilhaInconsistenteException : Exception
{
    public PlanilhaInconsistenteException(string message) : base(message) { }
}
