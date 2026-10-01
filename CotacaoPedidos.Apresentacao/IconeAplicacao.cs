namespace CotacaoPedidos.Apresentacao;

/// <summary>
/// Ícone do aplicativo (Recursos/icone.ico, embutido no programa) para a barra de título e a barra de tarefas.
/// O arquivo traz vários tamanhos (16 a 256 px): o Windows Forms escolhe o certo para cada lugar.
/// </summary>
internal static class IconeAplicacao
{
    private static Icon? _icone;

    public static Icon Obter()
    {
        if (_icone is not null)
            return _icone;

        using var recurso = typeof(IconeAplicacao).Assembly.GetManifestResourceStream("icone.ico")
            ?? throw new InvalidOperationException("O recurso embutido 'icone.ico' não foi encontrado.");

        return _icone = new Icon(recurso);
    }
}
