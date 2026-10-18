using System;
using System.IO;

namespace AutomacaoRPA.Services;

public static class DiretoriosAplicacao
{
    public static string ObterDiretorioPersistente()
    {
        DirectoryInfo? diretorio = new(
            AppContext.BaseDirectory
        );

        while (diretorio != null)
        {
            string projeto = Path.Combine(
                diretorio.FullName,
                "Automacao_Correios.csproj"
            );

            if (File.Exists(projeto))
                return diretorio.FullName;

            diretorio = diretorio.Parent;
        }

        return AppContext.BaseDirectory;
    }

    public static string ObterOuCriar(string nome)
    {
        string caminho = Path.Combine(
            ObterDiretorioPersistente(),
            nome
        );

        Directory.CreateDirectory(caminho);
        return caminho;
    }
}
