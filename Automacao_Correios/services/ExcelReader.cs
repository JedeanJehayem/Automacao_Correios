using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ClosedXML.Excel;

namespace AutomacaoRPA.Services;

public static class ExcelReader
{
    public static List<string> Read(string caminho)
    {
        if (!File.Exists(caminho))
            throw new FileNotFoundException(
                $"Excel não encontrado: {caminho}");

        using var workbook = new XLWorkbook(caminho);
        var planilha = workbook.Worksheet(1);

        // Validar cabeçalho
        string cabecalho = planilha.Cell(1, 1).GetString().Trim();

        if (!cabecalho.Equals(
            "Código de rastreamento",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception("Coluna de rastreamento não encontrada.");
        }

        // Ler códigos ignorando cabeçalho e linhas vazias
        return planilha.Column(1)
            .CellsUsed()
            .Skip(1)
            .Select(c => c.GetFormattedString().Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();
    }
}
