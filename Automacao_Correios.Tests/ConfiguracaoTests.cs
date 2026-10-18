using AutomacaoRPA.Services;

namespace Automacao_Correios.Tests;

public sealed class ConfiguracaoTests : IDisposable
{
    private readonly string _diretorio = Path.Combine(
        Path.GetTempPath(),
        $"automacao-correios-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void Carregar_DeveLerTodasAsOpcoes()
    {
        Directory.CreateDirectory(_diretorio);
        string caminho = Path.Combine(_diretorio, ".env");

        File.WriteAllText(
            caminho,
            "URL_CORREIOS=https://rastreamento.correios.com.br/app/index.php\n" +
            "MAX_TENTATIVAS_TRANSACAO=4\n" +
            "TIMEOUT_EVENTOS_SEGUNDOS=12\n" +
            "MANTER_NAVEGADOR_ABERTO=true\n"
        );

        Configuracao configuracao = Configuracao.Carregar(caminho);

        Assert.Equal(4, configuracao.MaxTentativasTransacao);
        Assert.Equal(12, configuracao.TimeoutEventosSegundos);
        Assert.True(configuracao.ManterNavegadorAberto);
    }

    public void Dispose()
    {
        if (Directory.Exists(_diretorio))
            Directory.Delete(_diretorio, true);
    }
}
