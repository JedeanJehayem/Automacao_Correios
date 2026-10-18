using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DdddOcr.Net;
using OpenCvSharp;

namespace AutomacaoRPA.Modulos;

public static class CorreiosCaptchaResolver
{
    private const int TamanhoMinimoCaptcha = 4;
    private const int TamanhoMaximoCaptcha = 6;

    private static readonly Lazy<DDDDOCR> MotorBeta =
        new(() => new DDDDOCR(DdddOcrMode.ClassifyBeta));

    private static readonly Lazy<DDDDOCR> MotorOld =
        new(() => new DDDDOCR(DdddOcrMode.ClassifyOld));

    private static readonly object Bloqueio = new();

    public static async Task<string> ReconhecerAsync(
        string caminhoImagem)
    {
        if (!File.Exists(caminhoImagem))
        {
            throw new FileNotFoundException(
                "Imagem não encontrada.",
                caminhoImagem);
        }

        Console.WriteLine(
            "[OCR] Gerando variantes e iniciando os modelos Beta e Old...");

        byte[] imagem = await File.ReadAllBytesAsync(
            caminhoImagem);

        List<VarianteImagem> variantes =
            GerarVariantes(imagem);

        string prefixoArquivos = ObterPrefixoArquivos(
            caminhoImagem);

        foreach (var variante in variantes.Where(variante =>
            variante.Nome != "original"))
        {
            await File.WriteAllBytesAsync(
                $"{prefixoArquivos}_{variante.Nome}.png",
                variante.Bytes
            );
        }

        List<PrevisaoOcr> previsoes = await Task.Run(() =>
        {
            lock (Bloqueio)
            {
                var resultados = new List<PrevisaoOcr>();

                foreach (VarianteImagem variante in variantes)
                {
                    Classificar(
                        resultados,
                        "beta",
                        variante,
                        MotorBeta.Value
                    );

                    Classificar(
                        resultados,
                        "old",
                        variante,
                        MotorOld.Value
                    );
                }

                return resultados;
            }
        });

        foreach (PrevisaoOcr previsao in previsoes)
        {
            Console.WriteLine(
                $"[OCR] {previsao.Modelo}/{previsao.Variante}: " +
                $"{previsao.TextoNormalizado}");
        }

        var previsoesValidas = previsoes
            .Where(previsao =>
                previsao.TextoNormalizado.Length >= TamanhoMinimoCaptcha &&
                previsao.TextoNormalizado.Length <= TamanhoMaximoCaptcha)
            .ToList();

        var candidatosCalculados = previsoesValidas
            .GroupBy(
                previsao => previsao.TextoNormalizado,
                StringComparer.OrdinalIgnoreCase)
            .Select(grupo => new
            {
                Texto = grupo.Key,
                Votos = grupo.Count(),
                Concordancia = previsoesValidas.Sum(previsao =>
                    CalcularConcordancia(
                        grupo.Key,
                        previsao.TextoNormalizado)),
                DistanciaTotal = previsoesValidas.Sum(previsao =>
                    CalcularDistanciaLevenshtein(
                        grupo.Key,
                        previsao.TextoNormalizado)),
                Modelos = grupo
                    .Select(previsao => previsao.Modelo)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                BetaOriginal = grupo.Any(previsao =>
                    previsao.Modelo == "beta" &&
                    previsao.Variante == "original"),
                TemBeta = grupo.Any(previsao =>
                    previsao.Modelo == "beta")
            })
            .ToList();

        var candidatosComRepeticao = candidatosCalculados
            .Where(candidato => candidato.Votos >= 2)
            .ToList();

        var candidatos = (candidatosComRepeticao.Count > 0
                ? candidatosComRepeticao
                : candidatosCalculados)
            .OrderByDescending(candidato => candidato.Concordancia)
            .ThenByDescending(candidato => candidato.Votos)
            .ThenByDescending(candidato => candidato.Modelos)
            .ThenBy(candidato => candidato.DistanciaTotal)
            .ThenByDescending(candidato => candidato.Texto.Length)
            .ThenByDescending(candidato => candidato.BetaOriginal)
            .ThenByDescending(candidato => candidato.TemBeta)
            .ToList();

        string caminhoDiagnostico =
            $"{prefixoArquivos}_resultados_ocr.txt";

        var diagnostico = previsoes.Select(previsao =>
                $"{previsao.Modelo}/{previsao.Variante}: " +
                $"bruto='{previsao.TextoBruto}' " +
                $"normalizado='{previsao.TextoNormalizado}'")
            .Concat([""])
            .Concat(candidatos.Select((candidato, indice) =>
                $"candidato {indice + 1}: '{candidato.Texto}' " +
                $"concordancia={candidato.Concordancia} " +
                $"votos={candidato.Votos} " +
                $"modelos={candidato.Modelos} " +
                $"distancia={candidato.DistanciaTotal}"));

        await File.WriteAllLinesAsync(
            caminhoDiagnostico,
            diagnostico,
            Encoding.UTF8
        );

        if (candidatos.Count == 0)
        {
            throw new InvalidOperationException(
                $"Nenhuma previsão do OCR possui entre " +
                $"{TamanhoMinimoCaptcha} e " +
                $"{TamanhoMaximoCaptcha} caracteres válidos.");
        }

        var vencedor = candidatos[0];

        Console.WriteLine(
            $"[OCR] Consenso escolhido: {vencedor.Texto} " +
            $"({vencedor.Votos} votos, {vencedor.Modelos} modelos, " +
            $"concordância {vencedor.Concordancia}).");

        Console.WriteLine(
            $"[OCR] Diagnóstico salvo: {caminhoDiagnostico}");

        return vencedor.Texto;
    }

    private static void Classificar(
        List<PrevisaoOcr> resultados,
        string modelo,
        VarianteImagem variante,
        DDDDOCR motor)
    {
        try
        {
            string bruto = motor.Classify(variante.Bytes).Trim();

            resultados.Add(new PrevisaoOcr(
                modelo,
                variante.Nome,
                bruto,
                Normalizar(bruto)
            ));
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[OCR] Falha em {modelo}/{variante.Nome}: " +
                ex.Message);
        }
    }

    private static List<VarianteImagem> GerarVariantes(
        byte[] imagem)
    {
        var variantes = new List<VarianteImagem>
        {
            new("original", imagem)
        };

        using Mat original = Cv2.ImDecode(
            imagem,
            ImreadModes.Color
        );

        if (original.Empty())
            return variantes;

        using var cinza = new Mat();
        Cv2.CvtColor(
            original,
            cinza,
            ColorConversionCodes.BGR2GRAY
        );
        AdicionarVariante(variantes, "cinza", cinza);

        using var contraste = new Mat();
        Cv2.EqualizeHist(cinza, contraste);
        AdicionarVariante(variantes, "contraste", contraste);

        using var suavizada = new Mat();
        Cv2.MedianBlur(contraste, suavizada, 3);

        using var otsu = new Mat();
        Cv2.Threshold(
            suavizada,
            otsu,
            0,
            255,
            ThresholdTypes.Binary | ThresholdTypes.Otsu
        );
        AdicionarVariante(variantes, "otsu", otsu);

        using var adaptativa = new Mat();
        Cv2.AdaptiveThreshold(
            suavizada,
            adaptativa,
            255,
            AdaptiveThresholdTypes.GaussianC,
            ThresholdTypes.Binary,
            31,
            7
        );
        AdicionarVariante(variantes, "adaptativa", adaptativa);

        AdicionarVariantesRecortadas(
            variantes,
            original
        );

        return variantes;
    }

    private static void AdicionarVariantesRecortadas(
        List<VarianteImagem> variantes,
        Mat original)
    {
        int margemX = Math.Max(4, original.Width / 25);
        int margemY = Math.Max(5, original.Height / 10);

        int largura = original.Width - (margemX * 2);
        int altura = original.Height - (margemY * 2);

        if (largura <= 0 || altura <= 0)
            return;

        using var recorte = new Mat(
            original,
            new Rect(margemX, margemY, largura, altura)
        ).Clone();

        AdicionarVariante(variantes, "recorte", recorte);

        using var recorteCinza = new Mat();
        Cv2.CvtColor(
            recorte,
            recorteCinza,
            ColorConversionCodes.BGR2GRAY
        );

        using var recorteContraste = new Mat();
        Cv2.EqualizeHist(recorteCinza, recorteContraste);
        AdicionarVariante(
            variantes,
            "recorte_contraste",
            recorteContraste
        );

        using var recorteSuavizado = new Mat();
        Cv2.MedianBlur(recorteContraste, recorteSuavizado, 3);

        using var recorteOtsu = new Mat();
        Cv2.Threshold(
            recorteSuavizado,
            recorteOtsu,
            0,
            255,
            ThresholdTypes.Binary | ThresholdTypes.Otsu
        );
        AdicionarVariante(
            variantes,
            "recorte_otsu",
            recorteOtsu
        );
    }

    private static int CalcularConcordancia(
        string primeiro,
        string segundo)
    {
        int comprimentoMaximo = Math.Max(
            primeiro.Length,
            segundo.Length
        );

        return comprimentoMaximo -
            CalcularDistanciaLevenshtein(primeiro, segundo);
    }

    internal static int CalcularDistanciaLevenshtein(
        string primeiro,
        string segundo)
    {
        var anterior = new int[segundo.Length + 1];
        var atual = new int[segundo.Length + 1];

        for (int coluna = 0;
             coluna <= segundo.Length;
             coluna++)
        {
            anterior[coluna] = coluna;
        }

        for (int linha = 1;
             linha <= primeiro.Length;
             linha++)
        {
            atual[0] = linha;

            for (int coluna = 1;
                 coluna <= segundo.Length;
                 coluna++)
            {
                int custo = primeiro[linha - 1] == segundo[coluna - 1]
                    ? 0
                    : 1;

                atual[coluna] = Math.Min(
                    Math.Min(
                        atual[coluna - 1] + 1,
                        anterior[coluna] + 1),
                    anterior[coluna - 1] + custo
                );
            }

            (anterior, atual) = (atual, anterior);
        }

        return anterior[segundo.Length];
    }

    private static void AdicionarVariante(
        List<VarianteImagem> variantes,
        string nome,
        Mat imagem)
    {
        using var imagemBgr = new Mat();

        if (imagem.Channels() == 1)
        {
            Cv2.CvtColor(
                imagem,
                imagemBgr,
                ColorConversionCodes.GRAY2BGR
            );
        }
        else
        {
            imagem.CopyTo(imagemBgr);
        }

        Cv2.ImEncode(".png", imagemBgr, out byte[] bytes);
        variantes.Add(new VarianteImagem(nome, bytes));
    }

    internal static string Normalizar(string texto)
    {
        return new string(texto
            .ToLowerInvariant()
            .Where(caractere =>
                caractere is >= 'a' and <= 'z' or
                    >= '0' and <= '9')
            .ToArray());
    }

    private static string ObterPrefixoArquivos(
        string caminhoImagem)
    {
        string diretorio = Path.GetDirectoryName(caminhoImagem)
            ?? AppContext.BaseDirectory;

        string nome = Path.GetFileNameWithoutExtension(
            caminhoImagem);

        if (nome.EndsWith(
                "_original",
                StringComparison.OrdinalIgnoreCase))
        {
            nome = nome[..^"_original".Length];
        }

        return Path.Combine(diretorio, nome);
    }

    public static void Encerrar()
    {
        if (MotorBeta.IsValueCreated)
            MotorBeta.Value.Dispose();

        if (MotorOld.IsValueCreated)
            MotorOld.Value.Dispose();
    }

    private sealed record VarianteImagem(
        string Nome,
        byte[] Bytes);

    private sealed record PrevisaoOcr(
        string Modelo,
        string Variante,
        string TextoBruto,
        string TextoNormalizado);
}
