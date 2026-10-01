using System.Globalization;

namespace CotacaoPedidos.Apresentacao;

/// <summary>
/// Caixa de texto que aceita só dígitos. Substitui o NumericUpDown: sem as setas, que alteravam o número
/// sem querer (rolagem do mouse, clique) e não fazem sentido para códigos como Filial e Sequência.
/// </summary>
internal sealed class CampoNumerico : TextBox
{
    public CampoNumerico(int maximoDigitos)
    {
        MaxLength = maximoDigitos;
    }

    /// <summary>O número digitado, ou null se o campo estiver vazio ou zerado.</summary>
    public int? Valor =>
        int.TryParse(Text, NumberStyles.None, CultureInfo.InvariantCulture, out var valor) && valor > 0 ? valor : null;

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar))
            e.Handled = true;

        base.OnKeyPress(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        // Colar (Ctrl+V) não passa pelo KeyPress: remove o que não for dígito.
        var digitos = string.Concat(Text.Where(char.IsAsciiDigit));
        if (digitos != Text)
        {
            Text = digitos; // dispara OnTextChanged de novo, já com o texto limpo
            SelectionStart = Text.Length;
            return;
        }

        base.OnTextChanged(e);
    }
}
