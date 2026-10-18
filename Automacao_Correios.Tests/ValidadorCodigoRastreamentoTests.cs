using AutomacaoRPA.Services;

namespace Automacao_Correios.Tests;

public sealed class ValidadorCodigoRastreamentoTests
{
    [Theory]
    [InlineData("NN437873753BR", "NN437873753BR")]
    [InlineData(" ap612167199br ", "AP612167199BR")]
    public void NormalizarEValidar_DeveAceitarFormatoSro(
        string entrada,
        string esperado)
    {
        Assert.Equal(
            esperado,
            ValidadorCodigoRastreamento.NormalizarEValidar(entrada)
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("AP612167199US")]
    public void NormalizarEValidar_DeveRejeitarFormatoInvalido(
        string entrada)
    {
        Assert.Throws<FalhaPermanenteTransacaoException>(() =>
            ValidadorCodigoRastreamento.NormalizarEValidar(entrada)
        );
    }
}
