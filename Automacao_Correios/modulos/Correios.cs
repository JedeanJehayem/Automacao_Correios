
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.Json;
using CefSharp;
using CefSharp.WinForms;

namespace AutomacaoRPA.Modulos;

public static class Correios
{
    public static async Task AguardarFormularioProntoAsync(
        ChromiumWebBrowser navegador)
    {
        Console.WriteLine(
            "[CORREIOS] Aguardando formulário e CAPTCHA ficarem prontos...");

        var tempo = Stopwatch.StartNew();

        while (tempo.Elapsed < TimeSpan.FromSeconds(15))
        {
            var resposta = await navegador.EvaluateScriptAsync("""
                (() => {
                    const objeto = document.querySelector('#objeto');
                    const captcha = document.querySelector('#captcha_image');
                    const botao = document.querySelector('#b-pesquisar');

                    if (!objeto || !captcha || !botao)
                        return false;

                    const area = captcha.getBoundingClientRect();
                    const estilo = getComputedStyle(captcha);

                    return captcha.complete &&
                        captcha.naturalWidth > 0 &&
                        captcha.naturalHeight > 0 &&
                        area.width > 0 &&
                        area.height > 0 &&
                        estilo.display !== 'none' &&
                        estilo.visibility !== 'hidden';
                })();
                """);

            if (resposta.Success && resposta.Result is true)
            {
                Console.WriteLine(
                    "[CORREIOS] Formulário e CAPTCHA prontos.");
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            "Formulário ou CAPTCHA não ficou pronto em 15 segundos.");
    }

    // Preencher código de rastreamento
    public static async Task PreencherRastreamento(
        ChromiumWebBrowser navegador,
        string codigoRastreamento)
    {
        Console.WriteLine(
            $"[CORREIOS] Inserindo código: {codigoRastreamento}");

        // Localizar, focar e limpar o campo
        var inicio = await navegador.EvaluateScriptAsync("""
            (() => {
                const campo = document.querySelector('#objeto');

                if (!campo)
                    return false;

                campo.focus();
                campo.value = '';

                campo.dispatchEvent(
                    new Event('input', { bubbles: true })
                );

                return true;
            })();
            """);

        if (!inicio.Success || inicio.Result is not true)
        {
            throw new Exception(
                "Campo de rastreamento não encontrado.");
        }

        // Digitação gradual
        foreach (char caractere in codigoRastreamento)
        {
            string caractereJson =
                JsonSerializer.Serialize(caractere.ToString());

            string script = $$"""
                (() => {
                    const campo =
                        document.querySelector('#objeto');

                    if (!campo)
                        return false;

                    campo.value += {{caractereJson}};

                    campo.dispatchEvent(
                        new Event('input', { bubbles: true })
                    );

                    return true;
                })();
                """;

            var resultado =
                await navegador.EvaluateScriptAsync(script);

            if (!resultado.Success ||
                resultado.Result is not true)
            {
                throw new Exception(
                    "Erro durante o preenchimento.");
            }

            await Task.Delay(
                Random.Shared.Next(100, 251));

            if (Random.Shared.Next(0, 5) == 0)
            {
                await Task.Delay(
                    Random.Shared.Next(200, 451));
            }
        }

        // Validar código preenchido
        string codigoJson =
            JsonSerializer.Serialize(codigoRastreamento);

        string validacao = $$"""
            (() => {
                const campo =
                    document.querySelector('#objeto');

                if (!campo)
                    return false;

                const valido =
                    campo.value === {{codigoJson}};

                if (valido) {
                    campo.dispatchEvent(
                        new Event('change', { bubbles: true })
                    );
                }

                return valido;
            })();
            """;

        var verificacao =
            await navegador.EvaluateScriptAsync(validacao);

        if (!verificacao.Success ||
            verificacao.Result is not true)
        {
            throw new Exception(
                "Código de rastreamento preenchido incorretamente.");
        }

        Console.WriteLine(
            $"[CORREIOS] Código preenchido: {codigoRastreamento}");
    }

    // Aguardar conclusão da consulta no navegador
    public static async Task ConsultarAsync(
        ChromiumWebBrowser navegador,
        string textoReconhecido,
        string codigoObjeto,
        int timeoutEventosSegundos)
    {
        if (!navegador.IsBrowserInitialized)
        {
            throw new InvalidOperationException(
                "Navegador não inicializado.");
        }

        Console.WriteLine(
            $"[CORREIOS] Previsão OCR: {textoReconhecido}");

        string captchaReconhecido = textoReconhecido.Trim();

        if (string.IsNullOrWhiteSpace(captchaReconhecido))
        {
            throw new ArgumentException(
                "O OCR não reconheceu um valor para o CAPTCHA.",
                nameof(textoReconhecido));
        }

        string captchaJson =
            JsonSerializer.Serialize(captchaReconhecido);

        // Preencher o CAPTCHA com o texto retornado pelo OCR
        string script = $$"""
            (() => {
                const captcha =
                    document.querySelector('#captcha');

                const botao =
                    document.querySelector('#b-pesquisar');

                if (!captcha || !botao)
                    return false;

                captcha.focus();

                const definicaoValor =
                    Object.getOwnPropertyDescriptor(
                        HTMLInputElement.prototype,
                        'value'
                    );

                definicaoValor.set.call(
                    captcha,
                    {{captchaJson}}
                );

                captcha.dispatchEvent(
                    new Event('input', { bubbles: true })
                );

                captcha.dispatchEvent(
                    new Event('change', { bubbles: true })
                );

                return captcha.value === {{captchaJson}};
            })();
            """;

        var verificacao =
            await navegador.EvaluateScriptAsync(script);

        if (!verificacao.Success ||
            verificacao.Result is not true)
        {
            throw new Exception(
                "Não foi possível preencher o campo CAPTCHA.");
        }

        Console.WriteLine(
            $"[CORREIOS] CAPTCHA preenchido pelo OCR: {captchaReconhecido}");

        string? assinaturaAnterior =
            await CorreiosResultado.ObterAssinaturaAsync(navegador);

        int esperaAntesDoClique =
            Random.Shared.Next(0, 2001);

        Console.WriteLine(
            $"[CORREIOS] Aguardando {esperaAntesDoClique} ms " +
            "antes de consultar.");

        await Task.Delay(esperaAntesDoClique);

        var clique = await navegador.EvaluateScriptAsync("""
            (() => {
                const botao =
                    document.querySelector('#b-pesquisar');

                if (!botao)
                    return false;

                botao.click();
                return true;
            })();
            """);

        if (!clique.Success || clique.Result is not true)
        {
            throw new Exception(
                "Não foi possível clicar no botão Consultar.");
        }

        Console.WriteLine(
            "[CORREIOS] Botão Consultar acionado automaticamente.");

        string caminhoResultado =
            await CorreiosResultado.CapturarESalvarAsync(
                navegador,
                codigoObjeto,
                assinaturaAnterior,
                timeoutEventosSegundos
            );

        Console.WriteLine(
            $"[CORREIOS] Resultado salvo: {caminhoResultado}");

        Console.WriteLine(
            "[CORREIOS] Consulta concluída; seguindo automaticamente.");
    }
}
