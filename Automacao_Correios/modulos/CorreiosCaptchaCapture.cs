
using CefSharp;
using CefSharp.WinForms;
using CefSharp.DevTools.Page;
using System;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutomacaoRPA.Modulos;

public static class CorreiosCaptchaCapture
{
    public static async Task<string> CapturarCaptchaAsync(
        ChromiumWebBrowser navegador,
        string diretorioDestino)
    {
        ArgumentNullException.ThrowIfNull(navegador);

        Console.WriteLine("[CAPTCHA] Aguardando imagem...");

        if (!navegador.IsBrowserInitialized)
            throw new Exception("Chromium não inicializado.");

        var tempo = Stopwatch.StartNew();

        const int timeoutMs = 15000;

        while (tempo.ElapsedMilliseconds < timeoutMs)
        {
            // Localizar e verificar a imagem
            string script = """
                (() => {
                    const img = document.querySelector('#captcha_image');

                    if (!img)
                        return "AGUARDANDO";

                    const estilo = getComputedStyle(img);
                    const rect = img.getBoundingClientRect();

                    if (estilo.display === 'none' ||
                        estilo.visibility === 'hidden' ||
                        rect.width <= 0 ||
                        rect.height <= 0)
                        return "AGUARDANDO";

                    if (!img.complete)
                        return "AGUARDANDO";

                    if (img.naturalWidth <= 0)
                        return "INVALIDA";

                    try {
                        const canvas = document.createElement('canvas');
                        canvas.width = img.naturalWidth;
                        canvas.height = img.naturalHeight;

                        const contexto = canvas.getContext('2d', {
                            willReadFrequently: true
                        });

                        contexto.drawImage(img, 0, 0);

                        const dataUrl = canvas.toDataURL('image/png');

                        return JSON.stringify({
                            width: img.naturalWidth,
                            height: img.naturalHeight,
                            base64: dataUrl.substring(
                                dataUrl.indexOf(',') + 1
                            )
                        });
                    }
                    catch (erro) {
                        img.scrollIntoView({
                            block: 'center',
                            inline: 'center'
                        });

                        const areaFallback =
                            img.getBoundingClientRect();

                        return JSON.stringify({
                            x: areaFallback.left + window.scrollX,
                            y: areaFallback.top + window.scrollY,
                            width: areaFallback.width,
                            height: areaFallback.height,
                            canvasError: String(erro)
                        });
                    }
                })();
                """;

            var resultado = await navegador.EvaluateScriptAsync(script);

            if (!resultado.Success)
                throw new Exception(
                    $"Erro ao localizar CAPTCHA: {resultado.Message}");

            string resposta = resultado.Result?.ToString() ?? "";

            if (resposta is "INVALIDA" or "AGUARDANDO")
            {
                await Task.Delay(250);
                continue;
            }

            using var json = JsonDocument.Parse(resposta);
            var area = json.RootElement;

            double largura = area.GetProperty("width").GetDouble();
            double altura = area.GetProperty("height").GetDouble();

            byte[] imagem;

            if (area.TryGetProperty("base64", out var base64Elemento) &&
                !string.IsNullOrWhiteSpace(base64Elemento.GetString()))
            {
                imagem = Convert.FromBase64String(
                    base64Elemento.GetString()!
                );

                Console.WriteLine(
                    "[CAPTCHA] Pixels originais obtidos pelo canvas.");
            }
            else
            {
                double x = area.GetProperty("x").GetDouble();
                double y = area.GetProperty("y").GetDouble();

                var viewport = new Viewport
                {
                    X = x,
                    Y = y,
                    Width = largura,
                    Height = altura,
                    Scale = 1
                };

                imagem = await navegador.CaptureScreenshotAsync(
                    format: CaptureScreenshotFormat.Png,
                    viewPort: viewport,
                    captureBeyondViewport: true
                );

                Console.WriteLine(
                    "[CAPTCHA] Canvas indisponível; usando screenshot.");
            }

            Console.WriteLine(
                $"[CAPTCHA] Dimensões: {largura}x{altura}");

            if (imagem.Length == 0)
                throw new Exception("Captura retornou imagem vazia.");

            Directory.CreateDirectory(diretorioDestino);

            string identificador =
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

            string caminho = Path.GetFullPath(
                Path.Combine(
                    diretorioDestino,
                    $"captcha_{identificador}_original.png"
                )
            );

            await File.WriteAllBytesAsync(caminho, imagem);

            Console.WriteLine(
                $"[CAPTCHA] Imagem salva: {caminho}");

            return caminho;
        }

        throw new TimeoutException(
            "CAPTCHA não ficou disponível em 15 segundos.");
    }
}
