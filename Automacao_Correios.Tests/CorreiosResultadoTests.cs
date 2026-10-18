using AutomacaoRPA.Modulos;

namespace Automacao_Correios.Tests;

public sealed class CorreiosResultadoTests
{
    [Fact]
    public void ConverterEvento_DeveMapearTransferencia()
    {
        var bruto = new CorreiosResultado.EventoBruto
        {
            Itens =
            [
                new() { Tipo = "head", Texto = "Objeto em transferência - por favor aguarde." },
                new() { Tipo = "content", Texto = "de Unidade de Tratamento, Rio de Janeiro" },
                new() { Tipo = "content", Texto = "para Unidade de Tratamento, São Paulo" },
                new() { Tipo = "content", Texto = "16/10/2026 14:13" }
            ]
        };

        var evento = CorreiosResultado.ConverterEvento(bruto);

        Assert.Equal("Objeto em transferência - por favor aguarde", evento.Status);
        Assert.Equal("Unidade de Tratamento, Rio de Janeiro", evento.Origem);
        Assert.Equal("Unidade de Tratamento, São Paulo", evento.Destino);
        Assert.Null(evento.Local);
        Assert.Equal(new DateTime(2026, 10, 16, 14, 13, 0), evento.DataHora);
    }

    [Fact]
    public void ConverterEvento_DeveIgnorarPesquisaDeSatisfacao()
    {
        var bruto = new CorreiosResultado.EventoBruto
        {
            Itens =
            [
                new() { Tipo = "head", Texto = "Objeto entregue ao destinatário." },
                new() { Tipo = "content", Texto = "Pela Unidade de Distribuição, São José do Rio Preto" },
                new() { Tipo = "head", Texto = "Queremos te ouvir!", TemLink = true },
                new() { Tipo = "content", Texto = "31/08/2026 16:04" }
            ]
        };

        var evento = CorreiosResultado.ConverterEvento(bruto);

        Assert.Equal("São José do Rio Preto", evento.Local);
        Assert.Null(evento.Descricao);
    }
}
