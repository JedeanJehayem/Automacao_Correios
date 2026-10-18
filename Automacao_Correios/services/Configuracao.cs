using System;
using System.Collections.Generic;
using System.IO;

namespace AutomacaoRPA.Services;

public sealed class Configuracao
{
    public string CorreiosUrl { get; private init; } = string.Empty;
    public int MaxTentativasTransacao { get; private init; }
    public int TimeoutEventosSegundos { get; private init; }
    public bool ManterNavegadorAberto { get; private init; }

    public static Configuracao Carregar(string caminho)
    {
        if (!File.Exists(caminho))
        {
            throw new FileNotFoundException(
                $"Arquivo de configuração não encontrado: {caminho}",
                caminho
            );
        }

        var valores = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (string linhaOriginal in File.ReadAllLines(caminho))
        {
            string linha = linhaOriginal.Trim();

            if (string.IsNullOrWhiteSpace(linha) ||
                linha.StartsWith('#'))
            {
                continue;
            }

            int separador = linha.IndexOf('=');

            if (separador <= 0)
            {
                throw new FormatException(
                    $"Linha inválida no arquivo .env: {linhaOriginal}");
            }

            string variavel = linha[..separador].Trim();
            string valor = linha[(separador + 1)..].Trim();

            valores[variavel] = valor;
        }

        if (!valores.TryGetValue("URL_CORREIOS", out string? url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? endereco) ||
            endereco.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "URL_CORREIOS não possui uma URL HTTP/HTTPS válida.");
        }

        if (!valores.TryGetValue(
                "MAX_TENTATIVAS_TRANSACAO",
                out string? tentativasTexto) ||
            !int.TryParse(tentativasTexto, out int tentativas) ||
            tentativas < 1)
        {
            throw new InvalidOperationException(
                "MAX_TENTATIVAS_TRANSACAO deve ser um inteiro maior que zero.");
        }

        if (!valores.TryGetValue(
                "TIMEOUT_EVENTOS_SEGUNDOS",
                out string? timeoutTexto) ||
            !int.TryParse(timeoutTexto, out int timeoutEventos) ||
            timeoutEventos < 1)
        {
            throw new InvalidOperationException(
                "TIMEOUT_EVENTOS_SEGUNDOS deve ser um inteiro maior que zero.");
        }

        bool manterNavegadorAberto = false;

        if (valores.TryGetValue(
                "MANTER_NAVEGADOR_ABERTO",
                out string? manterAbertoTexto) &&
            !bool.TryParse(manterAbertoTexto, out manterNavegadorAberto))
        {
            throw new InvalidOperationException(
                "MANTER_NAVEGADOR_ABERTO deve ser true ou false.");
        }

        return new Configuracao
        {
            CorreiosUrl = endereco.AbsoluteUri,
            MaxTentativasTransacao = tentativas,
            TimeoutEventosSegundos = timeoutEventos,
            ManterNavegadorAberto = manterNavegadorAberto
        };
    }
}
