
using AutomacaoRPA;
using AutomacaoRPA.Services;
using CefSharp.WinForms;
using System;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var logExecucao = LogExecucao.Inicializar();

        ArgumentosExecucao argumentos;

        try
        {
            argumentos = ArgumentosExecucao.Analisar(args);
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"[ARGUMENTOS] {ex.Message}");
            Environment.ExitCode = 2;
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Criar janela do navegador
        using var janela = new Form
        {
            Text = "Automação Correios - Chromium",
            Width = 1280,
            Height = 850,
            StartPosition = FormStartPosition.CenterScreen
        };

        // Inicializar Chromium
        var navegador = new ChromiumWebBrowser("about:blank")
        {
            Dock = DockStyle.Fill
        };

        janela.Controls.Add(navegador);

        // Instanciar automação
        var automacao = new Automacao(navegador, argumentos);

        // Executar automação após abrir a janela
        janela.Shown += async (_, _) =>
        {
            try
            {
                await automacao.Execute();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] {ex.Message}");
                Environment.ExitCode = 1;
            }
            finally
            {
                if (!automacao.ManterNavegadorAberto)
                    janela.BeginInvoke(janela.Close);
            }
        };

        Application.Run(janela);
    }
}
