using System;
using System.Collections.Generic;
using System.IO;

namespace AutomacaoRPA.Services;

public sealed class ArgumentosExecucao
{
    public IReadOnlyList<string> Codigos { get; private init; } = [];
    public string? CaminhoArquivo { get; private init; }

    public static ArgumentosExecucao Analisar(string[] args)
    {
        var codigos = new List<string>();
        string? caminhoArquivo = null;

        for (int indice = 0; indice < args.Length; indice++)
        {
            string argumento = args[indice];

            if (argumento.Equals("--codigo", StringComparison.OrdinalIgnoreCase))
            {
                codigos.Add(ObterValor(args, ref indice, argumento));
                continue;
            }

            if (argumento.Equals("--arquivo", StringComparison.OrdinalIgnoreCase))
            {
                caminhoArquivo = Path.GetFullPath(
                    ObterValor(args, ref indice, argumento)
                );
                continue;
            }

            throw new ArgumentException(
                $"Argumento desconhecido: {argumento}. " +
                "Use --codigo CODIGO ou --arquivo CAMINHO."
            );
        }

        if (codigos.Count > 0 && caminhoArquivo is not null)
        {
            throw new ArgumentException(
                "Use --codigo ou --arquivo, não os dois ao mesmo tempo."
            );
        }

        return new ArgumentosExecucao
        {
            Codigos = codigos,
            CaminhoArquivo = caminhoArquivo
        };
    }

    private static string ObterValor(
        string[] args,
        ref int indice,
        string argumento)
    {
        if (indice + 1 >= args.Length ||
            string.IsNullOrWhiteSpace(args[indice + 1]))
        {
            throw new ArgumentException(
                $"O argumento {argumento} exige um valor."
            );
        }

        indice++;
        return args[indice].Trim();
    }
}
