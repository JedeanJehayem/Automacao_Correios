using AutomacaoRPA.Modulos;

namespace Automacao_Correios.Tests;

public sealed class CorreiosCaptchaResolverTests
{
    [Fact]
    public void Normalizar_DeveManterSomenteLetrasENumeros()
    {
        Assert.Equal("ab12", CorreiosCaptchaResolver.Normalizar(" A-b 1@2 "));
    }

    [Theory]
    [InlineData("captcha", "captcha", 0)]
    [InlineData("gmbh", "gm8h", 1)]
    [InlineData("abc", "abcd", 1)]
    public void DistanciaLevenshtein_DeveCalcularDiferencas(
        string primeiro,
        string segundo,
        int esperado)
    {
        Assert.Equal(
            esperado,
            CorreiosCaptchaResolver.CalcularDistanciaLevenshtein(
                primeiro,
                segundo
            )
        );
    }
}
