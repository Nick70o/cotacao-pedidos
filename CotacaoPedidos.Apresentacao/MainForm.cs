using System.Globalization;
using System.Text;
using CotacaoPedidos.Negocio;

namespace CotacaoPedidos.Apresentacao;

/// <summary>
/// Tela principal do programa de Cotação de Pedidos. Aba Cotação: Localizar, Exportar, Sobrescrever, Importar,
/// Copiar link e Abrir planilha. Aba Histórico: planilhas ativas e arquivadas.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private const string TextoSobrescrita =
        "Esta ação arquivará a cotação atual e criará uma nova cotação com os dados atuais do pedido. " +
        "A planilha anterior não poderá mais ser utilizada para importação e o link enviado ao fornecedor " +
        "deixará de funcionar. Deseja continuar?";

    private readonly CampoNumerico _txtFilial;
    private readonly Button _btnLupaFilial;
    private readonly Label _lblNomeFilial;
    private readonly CampoNumerico _txtSequencia;
    private readonly Button _btnLocalizar;
    private readonly Label _lblStatusValor;
    private readonly LinkLabel _lnkPlanilha;
    private readonly Label _lblPrecos;
    private readonly Label _lblAviso;
    private readonly Button _btnExportar;
    private readonly Button _btnSobrescrever;
    private readonly Button _btnImportar;
    private readonly Button _btnCopiarLink;
    private readonly Button _btnAbrirPlanilha;
    private readonly TextBox _txtLog;
    private readonly HistoricoPanel _historico;

    private Aplicacao? _aplicacao;
    private IReadOnlyList<Filial> _filiais = [];

    /// <summary>Último "Localizar pedido". Null quando Filial/Sequência foram alteradas depois dele.</summary>
    private SituacaoCotacao? _situacao;

    private bool _ocupado;

    public MainForm()
    {
        Text = "Cotação de Pedidos";
        Icon = IconeAplicacao.Obter();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 600);
        Size = new Size(860, 680);

        // ---- Pedido ----
        _txtFilial = new CampoNumerico(maximoDigitos: 5) { Width = 70, Anchor = AnchorStyles.Left };
        _btnLupaFilial = new Button
        {
            Text = "\U0001F50D",
            Font = new Font("Segoe UI Symbol", 9f),
            Size = new Size(30, 25),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 3)
        };
        _lblNomeFilial = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
        _txtSequencia = new CampoNumerico(maximoDigitos: 9) { Width = 110, Anchor = AnchorStyles.Left };
        _btnLocalizar = CriarBotao("Localizar pedido", (_, _) => Localizar());
        _btnLocalizar.Margin = new Padding(8, 3, 3, 3);

        new ToolTip().SetToolTip(_btnLupaFilial, "Pesquisar filial (F2)");

        // Filial | campo | lupa | ? | Sequência | campo + ? | Localizar + ? | (espaço)
        var tblPedido = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 8, RowCount = 2 };
        for (var i = 0; i < 7; i++)
            tblPedido.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tblPedido.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        tblPedido.Controls.Add(new Label { Text = "Filial:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        tblPedido.Controls.Add(_txtFilial, 1, 0);
        tblPedido.Controls.Add(_btnLupaFilial, 2, 0);
        tblPedido.Controls.Add(new IconeAjuda("Filial", TextosAjuda.Filial) { Margin = new Padding(3, 0, 16, 0) }, 3, 0);
        tblPedido.Controls.Add(new Label { Text = "Sequência:", AutoSize = true, Anchor = AnchorStyles.Left }, 4, 0);
        tblPedido.Controls.Add(_txtSequencia.ComAjuda("Sequência", TextosAjuda.Sequencia), 5, 0);
        tblPedido.Controls.Add(_btnLocalizar.ComAjuda("Localizar pedido", TextosAjuda.Localizar), 6, 0);
        tblPedido.Controls.Add(_lblNomeFilial, 1, 1);
        tblPedido.SetColumnSpan(_lblNomeFilial, 7);

        var grpPedido = new GroupBox { Text = "Pedido", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        grpPedido.Controls.Add(tblPedido);

        // ---- Situação ----
        _lblStatusValor = new Label { Text = "-", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        _lnkPlanilha = new LinkLabel { Text = "-", AutoSize = true };
        _lnkPlanilha.LinkClicked += (_, _) => AbrirPlanilha();
        _lblPrecos = new Label { Text = "-", AutoSize = true };
        _lblAviso = new Label { AutoSize = true, ForeColor = Color.DarkRed, MaximumSize = new Size(760, 0), Visible = false };

        var tblStatus = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        tblStatus.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tblStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tblStatus.Controls.Add(new Label { Text = "Status:", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Anchor = AnchorStyles.Left }, 0, 0);
        tblStatus.Controls.Add(_lblStatusValor.ComAjuda("O que significa cada status", TextosAjuda.Status), 1, 0);
        tblStatus.Controls.Add(new Label { Text = "Planilha:", AutoSize = true }, 0, 1);
        tblStatus.Controls.Add(_lnkPlanilha, 1, 1);
        tblStatus.Controls.Add(new Label { Text = "Preços:", AutoSize = true }, 0, 2);
        tblStatus.Controls.Add(_lblPrecos, 1, 2);
        tblStatus.Controls.Add(_lblAviso, 0, 3);
        tblStatus.SetColumnSpan(_lblAviso, 2);

        var grpStatus = new GroupBox { Text = "Situação", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        grpStatus.Controls.Add(tblStatus);

        // ---- Botões ----
        _btnExportar = CriarBotao("Exportar", (_, _) => Exportar());
        _btnSobrescrever = CriarBotao("Sobrescrever", (_, _) => Sobrescrever());
        _btnImportar = CriarBotao("Importar", (_, _) => Importar());
        _btnCopiarLink = CriarBotao("Copiar link", (_, _) => CopiarLink());
        _btnAbrirPlanilha = CriarBotao("Abrir planilha", (_, _) => AbrirPlanilha());
        _btnCopiarLink.Margin = new Padding(24, 4, 4, 4);

        var pnlBotoes = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        pnlBotoes.Controls.AddRange(
        [
            _btnExportar.ComAjuda("Exportar", TextosAjuda.Exportar),
            _btnSobrescrever.ComAjuda("Sobrescrever", TextosAjuda.Sobrescrever),
            _btnImportar.ComAjuda("Importar", TextosAjuda.Importar),
            _btnCopiarLink.ComAjuda("Copiar link", TextosAjuda.CopiarLink),
            _btnAbrirPlanilha.ComAjuda("Abrir planilha", TextosAjuda.AbrirPlanilha)
        ]);

        // ---- Log ----
        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9f)
        };

        var layoutCotacao = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        layoutCotacao.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutCotacao.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutCotacao.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutCotacao.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layoutCotacao.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layoutCotacao.Controls.Add(grpPedido, 0, 0);
        layoutCotacao.Controls.Add(grpStatus, 0, 1);
        layoutCotacao.Controls.Add(pnlBotoes, 0, 2);
        layoutCotacao.Controls.Add(_txtLog, 0, 3);

        // ---- Abas ----
        _historico = new HistoricoPanel(() => Cotacao.ListarHistoricoAsync(), Log) { Dock = DockStyle.Fill };

        var abaCotacao = new TabPage("Cotação");
        abaCotacao.Controls.Add(layoutCotacao);
        var abaHistorico = new TabPage("Histórico");
        abaHistorico.Controls.Add(_historico);

        var abas = new TabControl { Dock = DockStyle.Fill };
        abas.TabPages.AddRange([abaCotacao, abaHistorico]);
        abas.Selected += async (_, e) =>
        {
            if (e.TabPage == abaHistorico)
                await _historico.CarregarSeNecessarioAsync();
        };
        Controls.Add(abas);

        // ---- Rodapé: versão/build do programa ----
        // Adicionado depois da área principal (Fill) para o WinForms reservar o rodapé primeiro.
        var rodape = new StatusStrip { SizingGrip = false };
        rodape.Items.Add(new ToolStripStatusLabel { Spring = true }); // empurra a build para a direita
        rodape.Items.Add(new ToolStripStatusLabel
        {
            Text = InformacaoBuild.Texto,
            ToolTipText = InformacaoBuild.Detalhes,
            ForeColor = SystemColors.GrayText
        });
        Controls.Add(rodape);

        // ---- Eventos ----
        _txtFilial.TextChanged += (_, _) => PedidoAlterado();
        _txtSequencia.TextChanged += (_, _) => PedidoAlterado();
        _txtFilial.KeyDown += CamposDoPedido_KeyDown;
        _txtSequencia.KeyDown += CamposDoPedido_KeyDown;
        _btnLupaFilial.Click += async (_, _) => await AbrirLupaFilialAsync();

        Shown += MainForm_Shown;
        FormClosed += (_, _) => _aplicacao?.Dispose();

        MostrarSituacao(null);
    }

    private static Button CriarBotao(string texto, EventHandler onClick)
    {
        var botao = new Button { Text = texto, AutoSize = true, Margin = new Padding(4), Padding = new Padding(6, 2, 6, 2) };
        botao.Click += onClick;
        return botao;
    }

    // ---------------------------------------------------------------------------------------------
    // Inicialização
    // ---------------------------------------------------------------------------------------------

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        try
        {
            _ocupado = true;
            AtualizarBotoes();
            Log($"Cotação de Pedidos - {InformacaoBuild.Texto}");
            Log("Inicializando serviços (ETrade + Google Sheets)...");
            _aplicacao = await Task.Run(() => Aplicacao.IniciarAsync());
            Log("Pronto. Informe Filial e Sequência e clique em \"Localizar pedido\" (ou tecle Enter).");
        }
        catch (Exception ex)
        {
            Log($"[ERRO na inicialização] {ex.Message}");
            MessageBox.Show(
                this,
                $"Não foi possível inicializar o programa:\n\n{ex.Message}",
                "Erro de inicialização",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _ocupado = false;
            AtualizarBotoes();
        }

        if (_aplicacao is not null)
            await CarregarFiliaisAsync();

        _txtFilial.Focus();
    }

    private async Task CarregarFiliaisAsync()
    {
        try
        {
            _filiais = await Cotacao.ListarFiliaisAsync();
            AtualizarNomeFilial();
        }
        catch (Exception ex)
        {
            Log($"Não foi possível carregar as filiais: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Campos do pedido e lupa
    // ---------------------------------------------------------------------------------------------

    private void CamposDoPedido_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F2 && sender == _txtFilial)
        {
            e.SuppressKeyPress = true;
            _btnLupaFilial.PerformClick();
        }
        else if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            if (_btnLocalizar.Enabled)
                Localizar();
        }
    }

    /// <summary>
    /// Filial/Sequência mudaram: o status na tela não vale mais para os números digitados. Os botões de ação
    /// ficam desabilitados até um novo "Localizar pedido".
    /// </summary>
    private void PedidoAlterado()
    {
        AtualizarNomeFilial();

        if (_situacao is not null &&
            (_situacao.Filial != _txtFilial.Valor || _situacao.Sequencia != _txtSequencia.Valor))
            MostrarSituacao(null);

        AtualizarBotoes();
    }

    private void AtualizarNomeFilial()
    {
        var codigo = _txtFilial.Valor;
        var filial = _filiais.FirstOrDefault(f => f.Codigo == codigo);

        _lblNomeFilial.Text = codigo is null || _filiais.Count == 0 ? ""
            : filial is null ? "Filial não cadastrada"
            : $"{filial.Nome} - {filial.Cidade}/{filial.Uf}";
    }

    private async Task AbrirLupaFilialAsync()
    {
        if (_filiais.Count == 0)
            await CarregarFiliaisAsync();

        if (_filiais.Count == 0)
            return;

        using var dialogo = new FilialLookupForm(_filiais, _txtFilial.Valor);
        if (dialogo.ShowDialog(this) != DialogResult.OK || dialogo.Selecionada is not { } filial)
            return;

        _txtFilial.Text = filial.Codigo.ToString(CultureInfo.InvariantCulture);
        _txtSequencia.Focus();
        _txtSequencia.SelectAll();
    }

    // ---------------------------------------------------------------------------------------------
    // Ações. Cada uma volta a consultar o banco/planilha antes de executar (a camada de Negócio valida tudo);
    // o estado da tela só decide quais botões ficam habilitados.
    // ---------------------------------------------------------------------------------------------

    private void Localizar() => Executar(LocalizarAsync);

    private async Task LocalizarAsync()
    {
        var (filial, sequencia) = PedidoInformado();
        var situacao = await Cotacao.LocalizarPedidoAsync(filial, sequencia);
        MostrarSituacao(situacao);

        Log($"Filial {filial} - Pedido {sequencia}: {situacao.Status.ParaTexto()}");
        foreach (var divergencia in situacao.Divergencias)
            Log($"  [DIVERGÊNCIA] {divergencia}");
    }

    private void Exportar() => Executar(async () =>
    {
        var (filial, sequencia) = PedidoInformado();
        var resultado = await Cotacao.ExportarAsync(filial, sequencia);

        if (!resultado.JaExistia)
        {
            Log($"Planilha criada: {resultado.Planilha.Nome}. Use \"Copiar link\" para enviar ao fornecedor.");
            await LocalizarAsync();
            return;
        }

        // Seção 19.1: não cria outra automaticamente; pergunta se deve sobrescrever.
        Log($"Já existe uma planilha ativa para este pedido: {resultado.Planilha.Nome}");
        var sobrescrever = Confirmar(
            "Já existe uma cotação criada para este pedido. Deseja sobrescrever a cotação existente?",
            TextoSobrescrita,
            "Sim — Sobrescrever",
            "Não — Manter cotação atual",
            TaskDialogIcon.Warning);

        if (sobrescrever)
            await SobrescreverConfirmadoAsync(filial, sequencia);
        else
        {
            Log("Mantida a cotação atual.");
            await LocalizarAsync();
        }
    });

    private void Sobrescrever() => Executar(async () =>
    {
        var (filial, sequencia) = PedidoInformado();
        var confirmado = Confirmar(
            "Sobrescrever a cotação deste pedido?",
            TextoSobrescrita,
            "Sim — Sobrescrever",
            "Não — Manter cotação atual",
            TaskDialogIcon.Warning);

        if (confirmado)
            await SobrescreverConfirmadoAsync(filial, sequencia);
    });

    private async Task SobrescreverConfirmadoAsync(int filial, int sequencia)
    {
        var nova = await Cotacao.SobrescreverAsync(filial, sequencia);
        Log($"Cotação anterior arquivada. Nova planilha: {nova.Nome}. Envie o novo link ao fornecedor.");
        await LocalizarAsync();
    }

    private void Importar() => Executar(async () =>
    {
        var (filial, sequencia) = PedidoInformado();

        // Seção 13: todas as validações antes de qualquer UPDATE; seção 14: conferência pelo comprador.
        var resumo = await Cotacao.PrepararImportacaoAsync(filial, sequencia);
        if (!ConfirmarImportacao(resumo))
        {
            Log("Importação cancelada na conferência.");
            return;
        }

        var resultado = await Cotacao.ImportarAsync(resumo);
        Log($"Importação concluída: {resultado.ItensAtualizados} itens, " +
            $"Total Produtos {Moeda(resultado.TotalProdutos)}, Total Final {Moeda(resultado.TotalFinal)}.");

        MostrarSituacao(null);
        if (resultado.AvisoPlanilha is { } aviso)
        {
            Log($"[ATENÇÃO] {aviso}");
            MessageBox.Show(this, aviso, "Cotação importada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            MessageBox.Show(this,
                "Cotação importada com sucesso. Pedido alterado para Operação 72 - Cotação Realizada.",
                "Cotação importada", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        await LocalizarAsync();
    });

    private bool ConfirmarImportacao(ResumoImportacao resumo)
    {
        var nomeFilial = _filiais.FirstOrDefault(f => f.Codigo == resumo.Pedido.Filial)?.Nome;

        var texto = new StringBuilder()
            .AppendLine($"Pedido: {resumo.Pedido.Sequencia}")
            .AppendLine($"Filial: {resumo.Pedido.Filial}{(nomeFilial is null ? "" : $" - {nomeFilial}")}")
            .AppendLine($"Quantidade total de produtos: {resumo.TotalProdutos}")
            .AppendLine($"Produtos com preço: {resumo.ComPreco}")
            .AppendLine($"Produtos sem preço: {resumo.SemPreco}")
            .AppendLine($"Produtos que serão zerados: {resumo.Zerados}")
            .AppendLine($"Valor total após a importação: {Moeda(resumo.ValorTotal)}");

        if (resumo.SemPreco > 0)
        {
            texto.AppendLine().AppendLine(resumo.SemPreco == 1
                ? "Existe 1 produto sem preço. Este produto terá o valor unitário, valor total e valor final " +
                  "alterados para R$ 0,00."
                : $"Existem {resumo.SemPreco} produtos sem preço. Estes produtos terão o valor unitário, " +
                  "valor total e valor final alterados para R$ 0,00.");
        }

        texto.AppendLine()
            .Append("Depois da importação o pedido passa para a Operação 72 e a planilha fica somente leitura " +
                    "para o fornecedor. Deseja continuar?");

        return Confirmar(
            "Conferência antes da importação",
            texto.ToString(),
            "Confirmar importação",
            "Cancelar",
            resumo.SemPreco > 0 ? TaskDialogIcon.Warning : TaskDialogIcon.Information);
    }

    private void CopiarLink()
    {
        if (_situacao?.Planilha is not { } planilha)
            return;

        try
        {
            Clipboard.SetText(LinkPlanilha.Obter(planilha.SpreadsheetId));
            Log($"Link copiado: {planilha.Nome}");
        }
        catch (Exception ex)
        {
            Log($"Não foi possível copiar o link: {ex.Message}");
        }
    }

    private void AbrirPlanilha()
    {
        if (_situacao?.Planilha is not { } planilha)
            return;

        try
        {
            Navegador.Abrir(LinkPlanilha.Obter(planilha.SpreadsheetId));
        }
        catch (Exception ex)
        {
            Log($"Não foi possível abrir o link: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers de UI
    // ---------------------------------------------------------------------------------------------

    /// <summary>Camada de Negócio. A tela não acessa banco nem Google diretamente.</summary>
    private CotacaoService Cotacao =>
        _aplicacao?.Cotacao ?? throw new InvalidOperationException("O programa ainda não foi inicializado.");

    private (int Filial, int Sequencia) PedidoInformado() =>
        _txtFilial.Valor is { } filial && _txtSequencia.Valor is { } sequencia
            ? (filial, sequencia)
            : throw new OperacaoBloqueadaException("Informe a Filial e a Sequência do pedido.");

    private async void Executar(Func<Task> acao)
    {
        if (_ocupado)
            return;

        try
        {
            _ocupado = true;
            UseWaitCursor = true;
            AtualizarBotoes();
            await acao();
        }
        catch (OperacaoBloqueadaException ex)
        {
            // Bloqueio de negócio: mostrar a mensagem ao comprador.
            Log($"[BLOQUEADO] {ex.Message}");
            MessageBox.Show(this, ex.Message, "Operação bloqueada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (PlanilhaInconsistenteException ex)
        {
            Log($"[INCONSISTÊNCIA] {ex.Message}");
            MessageBox.Show(this, ex.Message, "Planilha inconsistente", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            Log($"[ERRO] {ex.Message}");
            MessageBox.Show(this, $"Ocorreu um erro inesperado:\n\n{ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _ocupado = false;
            UseWaitCursor = false;
            AtualizarBotoes();
        }
    }

    /// <summary>Seção 5.3: cada botão só fica disponível quando a ação faz sentido para o pedido localizado.</summary>
    private void AtualizarBotoes()
    {
        var pronto = _aplicacao is not null && !_ocupado;

        // ReadOnly (e não Enabled) para o campo não perder o foco: o resultado chega para os números digitados.
        _txtFilial.ReadOnly = _ocupado;
        _txtSequencia.ReadOnly = _ocupado;
        _btnLupaFilial.Enabled = pronto;
        _btnLocalizar.Enabled = pronto && _txtFilial.Valor is not null && _txtSequencia.Valor is not null;
        _btnExportar.Enabled = pronto && _situacao?.PodeExportar == true;
        _btnSobrescrever.Enabled = pronto && _situacao?.PodeSobrescrever == true;
        _btnImportar.Enabled = pronto && _situacao?.PodeImportar == true;
        _btnCopiarLink.Enabled = _situacao?.TemPlanilha == true;
        _btnAbrirPlanilha.Enabled = _situacao?.TemPlanilha == true;
    }

    private void MostrarSituacao(SituacaoCotacao? situacao)
    {
        _situacao = situacao;

        _lblStatusValor.Text = situacao?.Status.ParaTexto() ?? "-";
        _lblStatusValor.ForeColor = situacao?.Status switch
        {
            StatusCotacao.CotacaoRealizada or StatusCotacao.CotacaoImportada => Color.DarkGreen,
            StatusCotacao.PedidoNaoLocalizado or StatusCotacao.PedidoCancelado => Color.DarkRed,
            _ => SystemColors.ControlText
        };

        _lnkPlanilha.Text = situacao?.Planilha?.Nome ?? "-";
        _lnkPlanilha.LinkArea = situacao?.Planilha is null ? new LinkArea(0, 0) : new LinkArea(0, _lnkPlanilha.Text.Length);

        var linhas = situacao?.Linhas ?? [];
        _lblPrecos.Text = situacao?.Planilha is null
            ? "-"
            : $"{linhas.Count(l => l.PrecoPreenchido)} de {linhas.Count} produtos com preço preenchido";

        var semPreco = linhas.Count(l => !l.PrecoPreenchido);
        string? aviso = situacao switch
        {
            { Divergencias.Count: > 0 } =>
                "A planilha não confere com o pedido no ETrade (detalhes no log). A importação está bloqueada: " +
                "use Sobrescrever para gerar uma nova cotação com os dados atuais do pedido.",
            { Status: StatusCotacao.CotacaoRealizada } when semPreco > 0 =>
                $"{semPreco} produto(s) ainda sem preço. Confirme com o fornecedor se ele terminou antes de " +
                "importar: depois da importação a planilha fica somente leitura para ele.",
            _ => null
        };
        _lblAviso.Text = aviso ?? "";
        _lblAviso.Visible = aviso is not null;

        AtualizarBotoes();
    }

    private bool Confirmar(string titulo, string texto, string textoSim, string textoNao, TaskDialogIcon icone)
    {
        var sim = new TaskDialogButton(textoSim);
        var nao = new TaskDialogButton(textoNao);

        var pagina = new TaskDialogPage
        {
            Caption = Text,
            Heading = titulo,
            Text = texto,
            Icon = icone,
            AllowCancel = true,
            Buttons = { sim, nao },
            DefaultButton = nao // Enter por engano não dispara a ação
        };

        return TaskDialog.ShowDialog(this, pagina) == sim;
    }

    private static string Moeda(decimal valor) => valor.ToString("C", PtBr);

    private void Log(string mensagem)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(mensagem));
            return;
        }

        _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {mensagem}{Environment.NewLine}");
    }
}
