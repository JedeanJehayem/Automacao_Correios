using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.WinForms;
using AutomacaoRPA.Services;

namespace AutomacaoRPA.Modulos;

public static partial class CorreiosResultado
{
    private static readonly JsonSerializerOptions OpcoesJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static async Task<string?> ObterAssinaturaAsync(
        ChromiumWebBrowser navegador)
    {
        var resposta = await navegador.EvaluateScriptAsync("""
            (() => {
                const lista = document.querySelector('ul.ship-steps');

                if (!lista)
                    return null;

                return lista.innerText
                    .replace(/\s+/g, ' ')
                    .trim();
            })();
            """);

        return resposta.Success
            ? resposta.Result as string
            : null;
    }

    public static async Task<string> CapturarESalvarAsync(
        ChromiumWebBrowser navegador,
        string codigoObjeto,
        string? assinaturaAnterior,
        int timeoutSegundos)
    {
        Console.WriteLine(
            "[RESULTADO] Aguardando os eventos de rastreamento...");

        RespostaPagina respostaPagina =
            await AguardarEventosAsync(
                navegador,
                codigoObjeto,
                assinaturaAnterior,
                timeoutSegundos
            );

        var resultado = new ResultadoRastreamento
        {
            CodigoObjeto = codigoObjeto,
            Eventos = respostaPagina.Eventos
                .Select(ConverterEvento)
                .Where(evento =>
                    !string.IsNullOrWhiteSpace(evento.Status))
                .ToList()
        };

        if (resultado.Eventos.Count == 0)
        {
            throw new Exception(
                "Nenhum evento de rastreamento foi encontrado.");
        }

        string diretorio =
            DiretoriosAplicacao.ObterOuCriar("Resultados");

        string nomeArquivo = string.Concat(
            codigoObjeto.Select(caractere =>
                Path.GetInvalidFileNameChars().Contains(caractere)
                    ? '_'
                    : caractere)
        );

        string caminho = Path.Combine(
            diretorio,
            $"{nomeArquivo}.json"
        );

        string json = JsonSerializer.Serialize(
            resultado,
            OpcoesJson
        );

        await File.WriteAllTextAsync(caminho, json);

        Console.WriteLine(
            $"[RESULTADO] {resultado.Eventos.Count} eventos extraídos.");

        return caminho;
    }

    private static async Task<RespostaPagina> AguardarEventosAsync(
        ChromiumWebBrowser navegador,
        string codigoObjeto,
        string? assinaturaAnterior,
        int timeoutSegundos)
    {
        string codigoJson = JsonSerializer.Serialize(codigoObjeto);
        string? ultimaAssinatura = null;
        int repeticoes = 0;
        bool expansaoVerificada = false;
        DateTime limite = DateTime.UtcNow.AddSeconds(timeoutSegundos);

        string script = $$"""
            (() => {
                const lista = document.querySelector('ul.ship-steps');
                const etapas = lista
                    ? Array.from(lista.querySelectorAll('li.step'))
                    : [];

                const normalizar = texto =>
                    (texto || '').replace(/\s+/g, ' ').trim();

                const textoPagina = normalizar(document.body.innerText);
                const textoMinusculo = textoPagina
                    .toLocaleLowerCase('pt-BR')
                    .normalize('NFD')
                    .replace(/[\u0300-\u036f]/g, '');

                const mensagensCaptcha = [
                    'captcha invalido',
                    'captcha incorreto',
                    'codigo de verificacao invalido',
                    'codigo de verificacao incorreto',
                    'caracteres da imagem estao incorretos'
                ];

                if (mensagensCaptcha.some(mensagem =>
                    textoMinusculo.includes(mensagem))) {
                    return JSON.stringify({
                        tipoErro: 'transitoria',
                        erroPagina: 'O CAPTCHA foi recusado pelos Correios.',
                        assinatura: '',
                        contemCodigo: false,
                        eventos: []
                    });
                }

                const mensagensPermanentes = [
                    'objeto nao encontrado',
                    'codigo de objeto invalido',
                    'codigo de rastreamento invalido',
                    'nenhum objeto encontrado'
                ];

                if (mensagensPermanentes.some(mensagem =>
                    textoMinusculo.includes(mensagem))) {
                    return JSON.stringify({
                        tipoErro: 'permanente',
                        erroPagina: 'Objeto não encontrado ou código inválido.',
                        assinatura: '',
                        contemCodigo: false,
                        eventos: []
                    });
                }

                const mensagensTemporarias = [
                    'servico temporariamente indisponivel',
                    'sistema temporariamente indisponivel',
                    'tente novamente mais tarde',
                    'erro interno do servidor'
                ];

                if (mensagensTemporarias.some(mensagem =>
                    textoMinusculo.includes(mensagem))) {
                    return JSON.stringify({
                        tipoErro: 'transitoria',
                        erroPagina: 'Portal dos Correios temporariamente indisponível.',
                        assinatura: '',
                        contemCodigo: false,
                        eventos: []
                    });
                }

                if (etapas.length === 0)
                    return null;

                const eventos = etapas.map(etapa => ({
                    itens: Array.from(
                        etapa.querySelectorAll('.step-content p.text')
                    ).map(paragrafo => ({
                        tipo: paragrafo.classList.contains('text-head')
                            ? 'head'
                            : 'content',
                        texto: normalizar(paragrafo.textContent),
                        temLink: paragrafo.querySelector('a') !== null
                    }))
                }));

                const assinatura = normalizar(lista.innerText);

                return JSON.stringify({
                    assinatura,
                    contemCodigo: textoPagina.includes({{codigoJson}}),
                    eventos
                });
            })();
            """;

        await Task.Delay(500);

        while (DateTime.UtcNow < limite)
        {
            var resposta =
                await navegador.EvaluateScriptAsync(script);

            if (resposta.Success && resposta.Result is string json)
            {
                var pagina = JsonSerializer.Deserialize<RespostaPagina>(
                    json,
                    OpcoesJson
                );

                if (pagina is not null &&
                    pagina.Eventos.Count > 0)
                {
                    if (!expansaoVerificada)
                    {
                        expansaoVerificada = true;

                        if (await ExpandirHistoricoAsync(navegador))
                        {
                            Console.WriteLine(
                                "[RESULTADO] Histórico adicional expandido.");
                            ultimaAssinatura = null;
                            repeticoes = 0;
                            await Task.Delay(500);
                            continue;
                        }
                    }

                    if (pagina.Assinatura == ultimaAssinatura)
                        repeticoes++;
                    else
                        repeticoes = 1;

                    ultimaAssinatura = pagina.Assinatura;

                    bool resultadoAtualizado =
                        pagina.ContemCodigo ||
                        string.IsNullOrEmpty(assinaturaAnterior) ||
                        pagina.Assinatura != assinaturaAnterior;

                    if (resultadoAtualizado && repeticoes >= 2)
                        return pagina;
                }

                if (!string.IsNullOrWhiteSpace(pagina?.ErroPagina))
                {
                    if (pagina.TipoErro == "permanente")
                    {
                        throw new FalhaPermanenteTransacaoException(
                            pagina.ErroPagina
                        );
                    }

                    throw new InvalidOperationException(
                        pagina.ErroPagina);
                }
            }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"Os eventos de rastreamento não foram carregados " +
            $"em {timeoutSegundos} segundos.");
    }

    private static async Task<bool> ExpandirHistoricoAsync(
        ChromiumWebBrowser navegador)
    {
        var resposta = await navegador.EvaluateScriptAsync("""
            (() => {
                const botao = document.querySelector('#a-ver-mais');

                if (!botao || botao.dataset.rpaExpandido === 'true')
                    return false;

                const estilo = getComputedStyle(botao);
                const area = botao.getBoundingClientRect();
                const visivel = estilo.display !== 'none' &&
                    estilo.visibility !== 'hidden' &&
                    area.width > 0 && area.height > 0;

                if (!visivel)
                    return false;

                botao.dataset.rpaExpandido = 'true';
                botao.click();
                return true;
            })();
            """);

        return resposta.Success && resposta.Result is true;
    }

    internal static EventoRastreamento ConverterEvento(
        EventoBruto eventoBruto)
    {
        var itens = eventoBruto.Itens
            .Where(item => !string.IsNullOrWhiteSpace(item.Texto))
            .ToList();

        var cabecalhos = itens
            .Where(item => item.Tipo == "head")
            .ToList();

        string status = cabecalhos
            .FirstOrDefault()?.Texto
            .Trim()
            .TrimEnd('.') ?? string.Empty;

        DateTime? dataHora = null;
        ItemBruto? itemData = itens.LastOrDefault(item =>
            DataHoraRegex().IsMatch(item.Texto));

        if (itemData is not null &&
            DateTime.TryParseExact(
                itemData.Texto,
                "dd/MM/yyyy HH:mm",
                CultureInfo.GetCultureInfo("pt-BR"),
                DateTimeStyles.None,
                out DateTime dataConvertida))
        {
            dataHora = DateTime.SpecifyKind(
                dataConvertida,
                DateTimeKind.Unspecified
            );
        }

        var conteudos = itens
            .Where(item =>
                item.Tipo == "content" &&
                item != itemData)
            .Select(item => item.Texto.Trim())
            .ToList();

        string? origem = conteudos
            .FirstOrDefault(texto =>
                texto.StartsWith("de ",
                    StringComparison.OrdinalIgnoreCase));

        string? destino = conteudos
            .FirstOrDefault(texto =>
                texto.StartsWith("para ",
                    StringComparison.OrdinalIgnoreCase));

        if (origem is not null)
            origem = origem[3..].Trim();

        if (destino is not null)
            destino = destino[5..].Trim();

        var conteudosRestantes = conteudos
            .Where(texto =>
                !texto.StartsWith("de ",
                    StringComparison.OrdinalIgnoreCase) &&
                !texto.StartsWith("para ",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        string? local = null;

        if (origem is null && destino is null &&
            conteudosRestantes.Count > 0)
        {
            local = NormalizarLocal(conteudosRestantes[0]);
            conteudosRestantes.RemoveAt(0);
        }

        var descricoes = cabecalhos
            .Skip(1)
            .Where(item => !EhPesquisaDeSatisfacao(item))
            .Select(item => item.Texto.Trim().TrimEnd('.'))
            .Concat(conteudosRestantes)
            .Where(texto => !string.IsNullOrWhiteSpace(texto))
            .ToList();

        return new EventoRastreamento
        {
            Status = status,
            Descricao = descricoes.Count > 0
                ? string.Join(" ", descricoes)
                : null,
            DataHora = dataHora,
            Local = local,
            Origem = origem,
            Destino = destino
        };
    }

    private static string NormalizarLocal(string local)
    {
        return PrefixoUnidadeRegex()
            .Replace(local.Trim(), string.Empty)
            .Trim();
    }

    private static bool EhPesquisaDeSatisfacao(ItemBruto item)
    {
        return item.TemLink &&
            (item.Texto.Contains(
                "Queremos te ouvir",
                StringComparison.OrdinalIgnoreCase) ||
             item.Texto.Contains(
                "survey",
                StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"^\d{2}/\d{2}/\d{4}\s+\d{2}:\d{2}$")]
    private static partial Regex DataHoraRegex();

    [GeneratedRegex(
        @"^(?:Pela|Na|No)\s+Unidade\s+[^,]+,\s*",
        RegexOptions.IgnoreCase)]
    private static partial Regex PrefixoUnidadeRegex();

    private sealed class RespostaPagina
    {
        public string? TipoErro { get; set; }
        public string? ErroPagina { get; set; }
        public string Assinatura { get; set; } = string.Empty;
        public bool ContemCodigo { get; set; }
        public List<EventoBruto> Eventos { get; set; } = [];
    }

    internal sealed class EventoBruto
    {
        public List<ItemBruto> Itens { get; set; } = [];
    }

    internal sealed class ItemBruto
    {
        public string Tipo { get; set; } = string.Empty;
        public string Texto { get; set; } = string.Empty;
        public bool TemLink { get; set; }
    }

    internal sealed class ResultadoRastreamento
    {
        public string CodigoObjeto { get; set; } = string.Empty;
        public List<EventoRastreamento> Eventos { get; set; } = [];
    }

    internal sealed class EventoRastreamento
    {
        public string Status { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public DateTime? DataHora { get; set; }
        public string? Local { get; set; }
        public string? Origem { get; set; }
        public string? Destino { get; set; }
    }
}
