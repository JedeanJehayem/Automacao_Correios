using AutomacaoRPA.Services;

namespace Automacao_Correios.Tests;

public sealed class ControleTransacoesTests : IDisposable
{
    private readonly string _diretorio = Path.Combine(
        Path.GetTempPath(),
        $"controle-correios-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void ObterPendentes_DeveRemoverSomenteCodigosOk()
    {
        string caminho = Path.Combine(_diretorio, "transacoes.xlsx");
        string codigoOk = "NN437873753BR";
        string codigoErro = "AP612167199BR";

        var iniciais = ControleTransacoes.ObterPendentes(
            caminho,
            [codigoOk, codigoErro]
        );

        Assert.Equal(2, iniciais.Count);

        ControleTransacoes.MarcarComoOk(caminho, codigoOk);
        ControleTransacoes.MarcarComoErro(caminho, codigoErro);

        var pendentes = ControleTransacoes.ObterPendentes(
            caminho,
            [codigoOk, codigoErro]
        );

        Assert.Equal([codigoErro], pendentes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_diretorio))
            Directory.Delete(_diretorio, true);
    }
}
