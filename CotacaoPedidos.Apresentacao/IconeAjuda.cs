using System.Drawing.Drawing2D;

namespace CotacaoPedidos.Apresentacao;

/// <summary>
/// Ícone "?" ao lado de uma opção da tela. Passar o mouse mostra a explicação; clicar abre a explicação
/// numa janela. Funciona mesmo quando a opção ao lado está desabilitada (botão desabilitado não mostra dica).
/// </summary>
internal sealed class IconeAjuda : Control
{
    private static readonly Font FonteInterrogacao = new("Segoe UI", 8.25f, FontStyle.Bold);

    private readonly string _titulo;
    private readonly string _texto;
    private readonly ToolTip _dica;
    private bool _mouseSobre;

    public IconeAjuda(string titulo, string texto)
    {
        _titulo = titulo;
        _texto = texto;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(18, 18);
        Margin = new Padding(3, 0, 0, 0);
        Anchor = AnchorStyles.Left;
        Cursor = Cursors.Help;
        TabStop = false;
        AccessibleName = $"Ajuda: {titulo}";
        AccessibleDescription = texto;
        AccessibleRole = AccessibleRole.PushButton;

        _dica = new ToolTip
        {
            ToolTipTitle = titulo,
            ToolTipIcon = ToolTipIcon.Info,
            AutoPopDelay = 30_000, // tempo para ler textos mais longos
            InitialDelay = 150,
            ReshowDelay = 100
        };
        _dica.SetToolTip(this, texto);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _dica.Hide(this);
        MessageBox.Show(FindForm(), _texto, _titulo, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _mouseSobre = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _mouseSobre = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var circulo = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var fundo = new SolidBrush(_mouseSobre ? SystemColors.HotTrack : SystemColors.Highlight))
            e.Graphics.FillEllipse(fundo, circulo);

        TextRenderer.DrawText(e.Graphics, "?", FonteInterrogacao, circulo, SystemColors.HighlightText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _dica.Dispose();
        base.Dispose(disposing);
    }
}

internal static class IconeAjudaExtensions
{
    /// <summary>Devolve um painel com o controle e o "?" lado a lado, para colocar no lugar do controle.</summary>
    public static Control ComAjuda(this Control controle, string titulo, string texto)
    {
        var margem = controle.Margin;
        var painel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Padding = Padding.Empty,
            Margin = new Padding(margem.Left, margem.Top, margem.Right + 8, margem.Bottom),
            Anchor = AnchorStyles.Left
        };
        painel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        painel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        controle.Margin = Padding.Empty;
        controle.Anchor = AnchorStyles.Left;
        painel.Controls.Add(controle, 0, 0);
        painel.Controls.Add(new IconeAjuda(titulo, texto), 1, 0);
        return painel;
    }
}
