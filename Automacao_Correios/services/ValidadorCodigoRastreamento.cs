using System.Text.RegularExpressions;

namespace AutomacaoRPA.Services;

public static partial class ValidadorCodigoRastreamento
{
    public static string NormalizarEValidar(string codigo)
    {
        string normalizado = codigo.Trim().ToUpperInvariant();

        if (!FormatoSroRegex().IsMatch(normalizado))
        {
            throw new FalhaPermanenteTransacaoException(
                $"Código de rastreamento inválido: '{codigo}'. " +
                "O formato esperado é AA123456789BR."
            );
        }

        return normalizado;
    }

    [GeneratedRegex(@"^[A-Z]{2}\d{9}BR$")]
    private static partial Regex FormatoSroRegex();
}
