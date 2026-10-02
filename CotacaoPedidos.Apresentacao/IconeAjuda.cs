using System.Drawing.Drawing2D;

namespace CotacaoPedidos.Apresentacao;

/// <summary>
/// Ícone "?" ao lado de uma opção da tela. Passar o mouse mostra a explicação; clicar abre a explicação
/// numa janela. Funciona mesmo quando a opção ao lado está desabilitada (botão desabilitado não mostra dica).
/// Discreto em repouso (só o contorno, em cinza) e destacado com a cor do logo ao passar o mouse.
/// </summary>
internal sealed class IconeAjuda : Control
{
    private static readonly FontFamily FamiliaInterrogacao = new("Segoe UI");
    private const float TamanhoInterrogacaoPx = 11f;
    private static readonly Color CorRepouso = Color.FromArgb(0x8A, 0x8A, 0x8A);
    private static readonly Color CorDestaque = Color.FromArgb(0x0B, 0x3F, 0x4A); // fundo do logo (icone.svg)

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
        Size = new Size(15, 15);
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

        // Em alto contraste do Windows, usa as cores do sistema em vez das fixas.
        var altoContraste = SystemInformation.HighContrast;
        var corRepouso = altoContraste ? SystemColors.ControlText : CorRepouso;
        var corDestaque = altoContraste ? SystemColors.Highlight : CorDestaque;

        // Meio pixel para dentro: o traço de 1 px cai inteiro sobre os pixels da borda.
        var circulo = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
        Color corTexto;
        if (_mouseSobre)
        {
            using var fundo = new SolidBrush(corDestaque);
            e.Graphics.FillEllipse(fundo, circulo);
            corTexto = altoContraste ? SystemColors.HighlightText : Color.White;
        }
        else
        {
            using var contorno = new Pen(corRepouso, 1f);
            e.Graphics.DrawEllipse(contorno, circulo);
            corTexto = corRepouso;
        }

        // O "?" é desenhado como forma (não como texto) para ficar centralizado pelo desenho da letra e sem
        // as franjas coloridas do ClearType.
        using var interrogacao = new GraphicsPath();
        interrogacao.AddString("?", FamiliaInterrogacao, (int)FontStyle.Bold, TamanhoInterrogacaoPx, PointF.Empty,
            StringFormat.GenericTypographic);
        var limites = interrogacao.GetBounds();
        using (var centralizar = new Matrix())
        {
            centralizar.Translate(circulo.X + (circulo.Width - limites.Width) / 2 - limites.X,
                                  circulo.Y + (circulo.Height - limites.Height) / 2 - limites.Y);
            interrogacao.Transform(centralizar);
        }
        using var pincel = new SolidBrush(corTexto);
        e.Graphics.FillPath(pincel, interrogacao);
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
