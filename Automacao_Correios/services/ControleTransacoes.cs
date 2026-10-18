using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace AutomacaoRPA.Services;

public static class ControleTransacoes
{
    private const string NomePlanilha = "Controle";
    private const string StatusOk = "ok";
    private const string StatusErro = "erro";

    public static void MesclarHistorico(
        string caminhoOrigem,
        string caminhoDestino)
    {
        if (!File.Exists(caminhoOrigem) ||
            Path.GetFullPath(caminhoOrigem).Equals(
                Path.GetFullPath(caminhoDestino),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(caminhoDestino) ??
            throw new InvalidOperationException(
                "Diretório do controle não foi informado."));

        using var origem = new XLWorkbook(caminhoOrigem);
        using var destino = File.Exists(caminhoDestino)
            ? new XLWorkbook(caminhoDestino)
            : new XLWorkbook();

        var planilhaOrigem = origem.Worksheets
            .FirstOrDefault(aba =>
                aba.Name.Equals(
                    NomePlanilha,
                    StringComparison.OrdinalIgnoreCase));

        if (planilhaOrigem == null)
            return;

        var planilhaDestino = destino.Worksheets
            .FirstOrDefault(aba =>
                aba.Name.Equals(
                    NomePlanilha,
                    StringComparison.OrdinalIgnoreCase))
            ?? destino.AddWorksheet(NomePlanilha);

        PrepararCabecalho(planilhaDestino);

        var linhasDestino = ObterLinhasPorCodigo(
            planilhaDestino
        );

        bool alterado = false;

        foreach (var linhaOrigem in planilhaOrigem.RowsUsed(
                     XLCellsUsedOptions.Contents))
        {
            if (linhaOrigem.RowNumber() < 2)
                continue;

            string codigo = linhaOrigem.Cell(1)
                .GetString()
                .Trim();

            if (string.IsNullOrWhiteSpace(codigo))
                continue;

            string statusOrigem = NormalizarStatus(
                linhaOrigem.Cell(2).GetString()
            );

            if (!linhasDestino.TryGetValue(
                    codigo,
                    out int linhaDestino))
            {
                linhaDestino = ProximaLinha(linhasDestino);
                planilhaDestino.Cell(linhaDestino, 1).Value = codigo;
                linhasDestino[codigo] = linhaDestino;
                alterado = true;
            }

            string statusDestino = NormalizarStatus(
                planilhaDestino.Cell(linhaDestino, 2)
                    .GetString()
            );

            if (PrioridadeStatus(statusOrigem) <=
                PrioridadeStatus(statusDestino))
            {
                continue;
            }

            planilhaDestino.Cell(linhaDestino, 2).Value =
                statusOrigem;
            alterado = true;
        }

        if (!File.Exists(caminhoDestino) || alterado)
        {
            AjustarVisual(planilhaDestino);
            destino.SaveAs(caminhoDestino);

            Console.WriteLine(
                "[CONTROLE] Histórico anterior mesclado no " +
                "arquivo persistente.");
        }
    }

    public static List<string> ObterPendentes(
        string caminho,
        IEnumerable<string> codigosOriginais)
    {
        var codigos = codigosOriginais
            .Where(codigo => !string.IsNullOrWhiteSpace(codigo))
            .Select(codigo => codigo.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Directory.CreateDirectory(
            Path.GetDirectoryName(caminho) ??
            throw new InvalidOperationException(
                "Diretório do controle não foi informado."));

        using var workbook = File.Exists(caminho)
            ? new XLWorkbook(caminho)
            : new XLWorkbook();

        var planilha = workbook.Worksheets
            .FirstOrDefault(aba =>
                aba.Name.Equals(
                    NomePlanilha,
                    StringComparison.OrdinalIgnoreCase))
            ?? workbook.AddWorksheet(NomePlanilha);

        PrepararCabecalho(planilha);

        var linhasPorCodigo = ObterLinhasPorCodigo(planilha);
        bool alterado = false;

        foreach (string codigo in codigos)
        {
            if (linhasPorCodigo.ContainsKey(codigo))
                continue;

            int linha = ProximaLinha(linhasPorCodigo);

            planilha.Cell(linha, 1).Value = codigo;
            planilha.Cell(linha, 2).Clear(XLClearOptions.Contents);
            linhasPorCodigo[codigo] = linha;
            alterado = true;
        }

        if (!File.Exists(caminho) || alterado)
        {
            AjustarVisual(planilha);
            workbook.SaveAs(caminho);
        }

        return codigos
            .Where(codigo =>
            {
                int linha = linhasPorCodigo[codigo];
                string status = planilha.Cell(linha, 2)
                    .GetString()
                    .Trim();

                return !status.Equals(
                    StatusOk,
                    StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    public static void MarcarComoOk(
        string caminho,
        string codigo)
    {
        AtualizarStatus(caminho, codigo, StatusOk);
    }

    public static void MarcarComoErro(
        string caminho,
        string codigo)
    {
        AtualizarStatus(caminho, codigo, StatusErro);
    }

    private static void AtualizarStatus(
        string caminho,
        string codigo,
        string status)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(caminho) ??
            throw new InvalidOperationException(
                "Diretório do controle não foi informado."));

        using var workbook = File.Exists(caminho)
            ? new XLWorkbook(caminho)
            : new XLWorkbook();

        var planilha = workbook.Worksheets
            .FirstOrDefault(aba =>
                aba.Name.Equals(
                    NomePlanilha,
                    StringComparison.OrdinalIgnoreCase))
            ?? workbook.AddWorksheet(NomePlanilha);

        PrepararCabecalho(planilha);

        var linhasPorCodigo = ObterLinhasPorCodigo(planilha);

        if (!linhasPorCodigo.TryGetValue(codigo, out int linha))
        {
            linha = ProximaLinha(linhasPorCodigo);

            planilha.Cell(linha, 1).Value = codigo;
        }

        planilha.Cell(linha, 2).Value = status;
        AjustarVisual(planilha);
        workbook.SaveAs(caminho);

        Console.WriteLine(
            $"[CONTROLE] {codigo} atualizado para: {status}");
    }

    private static Dictionary<string, int> ObterLinhasPorCodigo(
        IXLWorksheet planilha)
    {
        var resultado = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var linhaUsada in planilha.RowsUsed(
                     XLCellsUsedOptions.Contents))
        {
            int linha = linhaUsada.RowNumber();

            if (linha < 2)
                continue;

            string codigo = linhaUsada.Cell(1)
                .GetString()
                .Trim();

            if (!string.IsNullOrWhiteSpace(codigo) &&
                !resultado.ContainsKey(codigo))
            {
                resultado[codigo] = linha;
            }
        }

        return resultado;
    }

    private static int ProximaLinha(
        Dictionary<string, int> linhasPorCodigo)
    {
        return linhasPorCodigo.Count == 0
            ? 2
            : linhasPorCodigo.Values.Max() + 1;
    }

    private static string NormalizarStatus(string status)
    {
        string normalizado = status.Trim().ToLowerInvariant();

        return normalizado is StatusOk or StatusErro
            ? normalizado
            : string.Empty;
    }

    private static int PrioridadeStatus(string status)
    {
        return status switch
        {
            StatusOk => 2,
            StatusErro => 1,
            _ => 0
        };
    }

    private static void PrepararCabecalho(IXLWorksheet planilha)
    {
        planilha.Cell(1, 1).Value = "Código";
        planilha.Cell(1, 2).Value = "Status";
    }

    private static void AjustarVisual(IXLWorksheet planilha)
    {
        var cabecalho = planilha.Range("A1:B1");
        cabecalho.Style.Fill.BackgroundColor =
            XLColor.FromHtml("#1F4E78");
        cabecalho.Style.Font.FontColor = XLColor.White;
        cabecalho.Style.Font.Bold = true;
        cabecalho.Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;

        planilha.Column(1).Width = 28;
        planilha.Column(2).Width = 14;
        planilha.SheetView.FreezeRows(1);
        planilha.ShowGridLines = false;
    }
}
