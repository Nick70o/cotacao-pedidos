namespace CotacaoPedidos.Apresentacao;

internal static class Dialogos
{
    /// <summary>
    /// Pergunta de confirmação com os botões nomeados pela ação ("Sim — Excluir" em vez de só "Sim").
    /// O botão padrão é o de não fazer nada: um Enter por engano não dispara a ação.
    /// </summary>
    public static bool Confirmar(
        Control dono, string titulo, string texto, string textoSim, string textoNao, TaskDialogIcon icone)
    {
        var sim = new TaskDialogButton(textoSim);
        var nao = new TaskDialogButton(textoNao);

        var pagina = new TaskDialogPage
        {
            Caption = dono.FindForm()?.Text ?? "",
            Heading = titulo,
            Text = texto,
            Icon = icone,
            AllowCancel = true,
            Buttons = { sim, nao },
            DefaultButton = nao
        };

        return TaskDialog.ShowDialog(dono, pagina) == sim;
    }
}
