using System.Text;
using CotacaoPedidos.Negocio;

namespace CotacaoPedidos.Apresentacao;

/// <summary>
/// Aba Histórico: todas as planilhas de cotação no Drive (ativas e arquivadas por Sobrescrever),
/// com filtro por Filial/Sequência e exclusão. A fonte é o próprio Drive - não há registro paralelo (seção 18).
/// </summary>
internal sealed class HistoricoPanel : UserControl
{
    private const string FormatoDataHora = "dd/MM/yyyy HH:mm";

    private readonly Func<Task<IReadOnlyList<RegistroHistorico>>> _carregar;
    private readonly Func<string, Task<RegistroHistorico>> _excluir;
    private readonly Action<string> _log;

    private readonly CampoNumerico _txtFilial = new(maximoDigitos: 5) { Width = 70, Anchor = AnchorStyles.Left };
    private readonly CampoNumerico _txtSequencia = new(maximoDigitos: 9) { Width = 110, Anchor = AnchorStyles.Left };
    private readonly CheckBox _chkArquivadas = new()
    {
        Text = "Mostrar arquivadas", AutoSize = true, Checked = true, Anchor = AnchorStyles.Left
    };
    private readonly Button _btnAtualizar = new() { Text = "Atualizar", AutoSize = true };
    private readonly Button _btnAbrir = new() { Text = "Abrir planilha", AutoSize = true, Enabled = false };
    private readonly Button _btnCopiar = new() { Text = "Copiar link", AutoSize = true, Enabled = false };
    private readonly Button _btnExcluir = new()
    {
        Text = "Excluir planilha", AutoSize = true, Enabled = false, Margin = new Padding(16, 3, 3, 3)
    };
    private readonly Label _lblResumo = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly DataGridView _grade;

    private IReadOnlyList<RegistroHistorico> _registros = [];
    private bool _carregado;

    /// <summary>Carregando a lista ou excluindo: uma coisa por vez.</summary>
    private bool _ocupado;

    public HistoricoPanel(
        Func<Task<IReadOnlyList<RegistroHistorico>>> carregar,
        Func<string, Task<RegistroHistorico>> excluir,
        Action<string> log)
    {
        _carregar = carregar;
        _excluir = excluir;
        _log = log;

        _grade = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
        };
        AdicionarColuna("Situação", typeof(string));
        AdicionarColuna("Status", typeof(string));
        AdicionarColuna("Filial", typeof(int));
        AdicionarColuna("Pedido", typeof(int));
        AdicionarColuna("Criada em", typeof(DateTime), FormatoDataHora);
        AdicionarColuna("Última alteração", typeof(DateTime), FormatoDataHora);
        AdicionarColuna("Arquivo", typeof(string)).AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        _grade.SelectionChanged += (_, _) => AtualizarBotoes();
        _grade.MouseMove += (_, _) => DestravarCursorDaGrade();
        _grade.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) AbrirSelecionada(); };
        _grade.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Delete)
                return;
            e.Handled = true;
            await ExcluirSelecionadaAsync();
        };

        _txtFilial.TextChanged += (_, _) => Filtrar();
        _txtSequencia.TextChanged += (_, _) => Filtrar();
        _chkArquivadas.CheckedChanged += (_, _) => Filtrar();
        _btnAtualizar.Click += async (_, _) => await AtualizarAsync();
        _btnAbrir.Click += (_, _) => AbrirSelecionada();
        _btnCopiar.Click += (_, _) => CopiarLinkSelecionada();
        _btnExcluir.Click += async (_, _) => await ExcluirSelecionadaAsync();

        _chkArquivadas.Margin = new Padding(12, 3, 0, 3);

        var pnlFiltro = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        pnlFiltro.Controls.AddRange(
        [
            new Label { Text = "Filial:", AutoSize = true, Anchor = AnchorStyles.Left },
            _txtFilial,
            new Label { Text = "Sequência:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 0, 3, 0) },
            _txtSequencia.ComAjuda("Filtro", TextosAjuda.FiltroHistorico),
            _chkArquivadas.ComAjuda("Mostrar arquivadas", TextosAjuda.MostrarArquivadas),
            _btnAtualizar.ComAjuda("Atualizar", TextosAjuda.AtualizarHistorico)
        ]);

        var pnlRodape = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        pnlRodape.Controls.AddRange(
        [
            _btnAbrir.ComAjuda("Abrir planilha", TextosAjuda.AbrirPlanilhaHistorico),
            _btnCopiar.ComAjuda("Copiar link", TextosAjuda.CopiarLinkHistorico),
            _btnExcluir.ComAjuda("Excluir planilha", TextosAjuda.ExcluirPlanilhaHistorico),
            _lblResumo
        ]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(pnlFiltro, 0, 0);
        layout.Controls.Add(_grade, 0, 1);
        layout.Controls.Add(pnlRodape, 0, 2);
        Controls.Add(layout);
    }

    private DataGridViewColumn AdicionarColuna(string titulo, Type tipo, string? formato = null)
    {
        var coluna = new DataGridViewTextBoxColumn
        {
            HeaderText = titulo,
            ValueType = tipo,
            SortMode = DataGridViewColumnSortMode.Automatic
        };
        if (formato is not null)
            coluna.DefaultCellStyle.Format = formato;

        _grade.Columns.Add(coluna);
        return coluna;
    }

    /// <summary>Carrega na primeira vez que a aba é aberta; depois, só pelo botão Atualizar.</summary>
    public async Task CarregarSeNecessarioAsync()
    {
        if (!_carregado)
            await AtualizarAsync();
    }

    private async Task AtualizarAsync()
    {
        if (_ocupado)
            return;

        try
        {
            DefinirOcupado(true);
            _registros = await _carregar();
            _carregado = true;
            Filtrar();
        }
        catch (Exception ex)
        {
            _log($"[ERRO] Não foi possível carregar o histórico: {ex.Message}");
            MessageBox.Show(this, $"Não foi possível carregar o histórico:\n\n{ex.Message}", "Histórico",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            DefinirOcupado(false);
        }
    }

    private void DefinirOcupado(bool ocupado)
    {
        _ocupado = ocupado;
        _btnAtualizar.Enabled = !ocupado;
        UseWaitCursor = ocupado;
        DestravarCursorDaGrade();
        AtualizarBotoes();
    }

    /// <summary>
    /// Defeito do DataGridView: se o mouse passa pela divisa de uma coluna (onde aparece o cursor de redimensionar)
    /// enquanto o cursor de espera está ativo, a grade guarda o cursor de espera como se fosse o dela e o devolve
    /// depois - ele fica girando sobre a grade mesmo com tudo carregado. Aqui ele volta ao normal.
    /// </summary>
    private void DestravarCursorDaGrade()
    {
        if (!_grade.UseWaitCursor && _grade.Cursor == Cursors.WaitCursor)
            _grade.Cursor = Cursors.Default;
    }

    private void Filtrar()
    {
        var filial = _txtFilial.Valor;
        var sequencia = _txtSequencia.Valor;
        var mostrarArquivadas = _chkArquivadas.Checked;

        var visiveis = _registros
            .Where(r => (filial is null || r.Filial == filial) &&
                        (sequencia is null || r.Sequencia == sequencia) &&
                        (mostrarArquivadas || !r.Arquivada))
            .ToList();

        _grade.SuspendLayout();
        _grade.Rows.Clear();
        foreach (var registro in visiveis)
        {
            // Célula nula aparece vazia e ordena antes das preenchidas.
            var indice = _grade.Rows.Add(
                registro.Arquivada ? "Arquivada" : "Ativa",
                registro.Status,
                registro.Filial!,
                registro.Sequencia!,
                registro.CriadaEm!,
                registro.AlteradaEm!,
                registro.Nome);

            var linha = _grade.Rows[indice];
            linha.Tag = registro;
            if (registro.Arquivada)
                linha.DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }
        _grade.ResumeLayout();

        _lblResumo.Text = _carregado ? $"{visiveis.Count} de {_registros.Count} planilha(s)" : "";
        AtualizarBotoes();
    }

    private RegistroHistorico? Selecionada =>
        _grade.SelectedRows.Count > 0 ? _grade.SelectedRows[0].Tag as RegistroHistorico : null;

    private void AtualizarBotoes()
    {
        var selecionada = Selecionada;
        _btnAbrir.Enabled = selecionada is not null;
        _btnCopiar.Enabled = selecionada is not null;
        _btnExcluir.Enabled = !_ocupado && selecionada is { PodeExcluir: true };
    }

    private void AbrirSelecionada()
    {
        if (Selecionada is not { } registro)
            return;

        try
        {
            Navegador.Abrir(registro.Link);
        }
        catch (Exception ex)
        {
            _log($"Não foi possível abrir o link: {ex.Message}");
        }
    }

    private void CopiarLinkSelecionada()
    {
        if (Selecionada is not { } registro)
            return;

        try
        {
            Clipboard.SetText(registro.Link);
            _log($"Link copiado: {registro.Nome}");
        }
        catch (Exception ex)
        {
            _log($"Não foi possível copiar o link: {ex.Message}");
        }
    }

    private async Task ExcluirSelecionadaAsync()
    {
        if (_ocupado || Selecionada is not { PodeExcluir: true } registro || !ConfirmarExclusao(registro))
            return;

        try
        {
            DefinirOcupado(true);
            var excluida = await _excluir(registro.SpreadsheetId);
            _log($"Planilha enviada para a lixeira do Google Drive: {excluida.Nome}");
        }
        catch (OperacaoBloqueadaException ex)
        {
            _log($"[BLOQUEADO] {ex.Message}");
            MessageBox.Show(this, ex.Message, "Exclusão bloqueada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _log($"[ERRO] Não foi possível excluir a planilha: {ex.Message}");
            MessageBox.Show(this, $"Não foi possível excluir a planilha:\n\n{ex.Message}", "Excluir planilha",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            DefinirOcupado(false);
        }

        // Recarrega também depois de um bloqueio: ele indica que a lista estava desatualizada.
        await AtualizarAsync();
    }

    private bool ConfirmarExclusao(RegistroHistorico registro)
    {
        var texto = new StringBuilder().AppendLine(registro.Nome).AppendLine();

        if (registro.Arquivada)
        {
            texto.AppendLine("É uma planilha arquivada, guardada só para consulta.");
        }
        else
        {
            texto.AppendLine("É a planilha ATIVA do pedido: o link enviado ao fornecedor deixa de funcionar e o " +
                             "pedido fica sem planilha (Cotação Pendente). Para cotar de novo, use Exportar.");
            if (registro.Status == NomesStatus.CotacaoRealizada)
                texto.AppendLine("O fornecedor já preencheu preços nesta planilha; eles não serão importados.");
        }

        texto.AppendLine()
            .Append("A planilha vai para a lixeira do Google Drive, onde pode ser restaurada em até 30 dias. " +
                    "Deseja excluir?");

        return Dialogos.Confirmar(this, "Excluir esta planilha?", texto.ToString(),
            "Sim — Excluir", "Não — Manter", TaskDialogIcon.Warning);
    }
}
