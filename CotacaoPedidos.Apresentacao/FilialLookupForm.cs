using System.Globalization;
using CotacaoPedidos.Negocio;

namespace CotacaoPedidos.Apresentacao;

/// <summary>Lupa da Filial: lista as filiais do ETrade com busca por código, nome ou cidade.</summary>
internal sealed class FilialLookupForm : Form
{
    private readonly IReadOnlyList<Filial> _filiais;
    private readonly TextBox _txtBusca;
    private readonly ListView _lista;

    public Filial? Selecionada { get; private set; }

    public FilialLookupForm(IReadOnlyList<Filial> filiais, int? filialAtual)
    {
        _filiais = filiais;

        Text = "Pesquisar filial";
        Icon = IconeAplicacao.Obter();
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 400);
        MinimumSize = new Size(420, 260);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        _lista = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true
        };
        _lista.Columns.Add("Código", 70, HorizontalAlignment.Right);
        _lista.Columns.Add("Nome", 320);
        _lista.Columns.Add("Cidade", 130);
        _lista.Columns.Add("UF", 40);
        _lista.DoubleClick += (_, _) => Confirmar();

        _txtBusca = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Digite o código, nome ou cidade" };
        _txtBusca.TextChanged += (_, _) => Filtrar(null);
        _txtBusca.KeyDown += (_, e) =>
        {
            // Setas na busca movem a seleção da lista, sem precisar tirar o foco do texto.
            if (e.KeyCode is not (Keys.Down or Keys.Up) || _lista.Items.Count == 0)
                return;

            var atual = _lista.SelectedIndices.Count > 0 ? _lista.SelectedIndices[0] : -1;
            var novo = Math.Clamp(atual + (e.KeyCode == Keys.Down ? 1 : -1), 0, _lista.Items.Count - 1);
            SelecionarItem(novo);
            e.Handled = true;
        };

        var btnSelecionar = new Button { Text = "Selecionar", AutoSize = true };
        btnSelecionar.Click += (_, _) => Confirmar();
        var btnCancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };

        var pnlBotoes = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft
        };
        pnlBotoes.Controls.AddRange([btnCancelar, btnSelecionar]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_txtBusca, 0, 0);
        layout.Controls.Add(_lista, 0, 1);
        layout.Controls.Add(pnlBotoes, 0, 2);
        Controls.Add(layout);

        AcceptButton = btnSelecionar;
        CancelButton = btnCancelar;

        Filtrar(filialAtual);
    }

    private void Filtrar(int? selecionar)
    {
        var termo = _txtBusca.Text.Trim();

        var visiveis = _filiais.Where(f =>
            termo.Length == 0 ||
            f.Codigo.ToString(CultureInfo.InvariantCulture).StartsWith(termo, StringComparison.Ordinal) ||
            f.Nome.Contains(termo, StringComparison.CurrentCultureIgnoreCase) ||
            f.Cidade.Contains(termo, StringComparison.CurrentCultureIgnoreCase));

        _lista.BeginUpdate();
        _lista.Items.Clear();
        foreach (var filial in visiveis)
        {
            var item = new ListViewItem(filial.Codigo.ToString(CultureInfo.InvariantCulture)) { Tag = filial };
            item.SubItems.Add(filial.Nome);
            item.SubItems.Add(filial.Cidade);
            item.SubItems.Add(filial.Uf);
            _lista.Items.Add(item);
        }
        _lista.EndUpdate();

        if (_lista.Items.Count == 0)
            return;

        var indice = 0;
        if (selecionar is not null)
        {
            var encontrado = _lista.Items.Cast<ListViewItem>()
                .FirstOrDefault(i => ((Filial)i.Tag!).Codigo == selecionar);
            if (encontrado is not null)
                indice = encontrado.Index;
        }
        SelecionarItem(indice);
    }

    private void SelecionarItem(int indice)
    {
        _lista.SelectedIndices.Clear();
        _lista.Items[indice].Selected = true;
        _lista.Items[indice].Focused = true;
        _lista.EnsureVisible(indice);
    }

    private void Confirmar()
    {
        if (_lista.SelectedItems.Count == 0)
            return;

        Selecionada = (Filial)_lista.SelectedItems[0].Tag!;
        DialogResult = DialogResult.OK;
        Close();
    }
}
