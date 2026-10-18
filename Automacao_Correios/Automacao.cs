
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutomacaoRPA.Services;
using AutomacaoRPA.Modulos;
using CefSharp;
using CefSharp.WinForms;

namespace AutomacaoRPA;

public class Automacao
{
    // Navegador Chromium
    private readonly ChromiumWebBrowser _navegador;
    private readonly ArgumentosExecucao _argumentos;

    // Fila de transações do Excel
    private Queue<string>? _filaTransacoes;

    private Configuracao? _configuracao;
    private int _tentativasOcr;
    private int _consultasConcluidas;
    private string? _caminhoArquivoAtivo;

    public bool ManterNavegadorAberto =>
        _configuracao?.ManterNavegadorAberto ?? false;

    private string CaminhoInput => Path.Combine(
        DiretoriosAplicacao.ObterDiretorioPersistente(),
        "input",
        "transacoes.xlsx"
    );

    private string CaminhoInputDistribuido => Path.Combine(
        AppContext.BaseDirectory,
        "input",
        "transacoes.xlsx"
    );

    private string CaminhoArquivoAtivo => _caminhoArquivoAtivo ??
        throw new InvalidOperationException(
            "Nenhuma transação foi obtida ainda.");

    // Construtor
    public Automacao(
        ChromiumWebBrowser navegador,
        ArgumentosExecucao argumentos)
    {
        _navegador = navegador;
        _argumentos = argumentos;
    }

    // ETAPA 1 - Inicialização
    public async Task StartupRoutine()
    {
        Console.WriteLine("[STARTUP] Inicializando automação...");

        _navegador.LoadError += (_, e) =>
        {
            if (e.Frame.IsMain &&
                e.ErrorCode != CefErrorCode.Aborted)
            {
                Console.WriteLine(
                    $"[CEF ERROR] Código: {e.ErrorCode}");

                Console.WriteLine(
                    $"[CEF ERROR] Descrição: {e.ErrorText}");

                Console.WriteLine(
                    $"[CEF ERROR] URL: {e.FailedUrl}");
            }
        };

        Console.WriteLine("[STARTUP] Aguardando Chromium...");

        await _navegador.WaitForInitialLoadAsync();

        Console.WriteLine("[STARTUP] Abrindo site dos Correios...");

        var resultado = await _navegador.LoadUrlAsync(
            ObterConfiguracao().CorreiosUrl
        );

        Console.WriteLine(
            $"[STARTUP] Erro: {resultado.ErrorCode}");

        Console.WriteLine(
            $"[STARTUP] HTTP: {resultado.HttpStatusCode}");

        if (resultado.ErrorCode != CefErrorCode.None ||
            resultado.HttpStatusCode >= 400)
        {
            throw new Exception(
                $"Falha ao abrir o site: {resultado.ErrorCode}; " +
                $"HTTP {resultado.HttpStatusCode}.");
        }

        Console.WriteLine("[STARTUP] Site carregado com sucesso!");
    }

    // ETAPA 2 - Obter transação
    public Task<string?> GetTransaction()
    {
        Console.WriteLine("[GET] Buscando transação...");

        if (_filaTransacoes == null)
        {
            bool usaArquivoPadrao = _argumentos.CaminhoArquivo is null;

            _caminhoArquivoAtivo = _argumentos.CaminhoArquivo ??
                CaminhoInput;

            if (usaArquivoPadrao)
            {
                ControleTransacoes.MesclarHistorico(
                    CaminhoInputDistribuido,
                    CaminhoInput
                );
            }

            Console.WriteLine(
                $"[CONTROLE] Planilha de entrada: {_caminhoArquivoAtivo}");

            IReadOnlyList<string> codigos;

            if (_argumentos.Codigos.Count > 0)
            {
                codigos = _argumentos.Codigos;
                Console.WriteLine(
                    "[GET] Usando códigos recebidos pela linha de comando.");
            }
            else
            {
                codigos = ExcelReader.Read(_caminhoArquivoAtivo);
                Console.WriteLine($"[GET] Entrada: {_caminhoArquivoAtivo}");
            }

            var codigosPendentes =
                ControleTransacoes.ObterPendentes(
                    _caminhoArquivoAtivo,
                    codigos
                );

            _filaTransacoes = new Queue<string>(codigosPendentes);

            Console.WriteLine(
                $"[GET] {_filaTransacoes.Count} transações pendentes carregadas.");
        }

        if (_filaTransacoes.Count == 0)
        {
            Console.WriteLine("[GET] Nenhuma transação pendente.");

            return Task.FromResult<string?>(null);
        }

        string codigo = _filaTransacoes.Dequeue();

        Console.WriteLine(
            $"[GET] Código de rastreamento: {codigo}");

        Console.WriteLine(
            $"[GET] Transações restantes: {_filaTransacoes.Count}");

        return Task.FromResult<string?>(codigo);
    }

    // ETAPA 3 - Processar transação
    public async Task ProcessTransaction(string transaction)
    {
        Console.WriteLine(
            $"[PROCESS] Iniciando processamento: {transaction}");

        await Correios.AguardarFormularioProntoAsync(
            _navegador
        );

        // 1. Preencher código de rastreamento
        await Correios.PreencherRastreamento(
            _navegador,
            transaction
        );

        // 2. Capturar imagem do CAPTCHA
        string imagemCaptcha =
            await CorreiosCaptchaCapture.CapturarCaptchaAsync(
                _navegador,
                DiretoriosAplicacao.ObterOuCriar("Captchas")
            );

        Console.WriteLine(
            $"[PROCESS] CAPTCHA salvo: {imagemCaptcha}");

        // 3. Reconhecimento OCR
        _tentativasOcr++;

        string textoReconhecido =
            await CorreiosCaptchaResolver.ReconhecerAsync(
                imagemCaptcha
            );

        Console.WriteLine(
            $"[PROCESS] Previsão OCR: {textoReconhecido}");

        if (string.IsNullOrWhiteSpace(textoReconhecido))
        {
            Console.WriteLine(
                "[OCR] Nenhum caractere reconhecido.");
        }

        // 4. Aguardar conclusão da consulta no navegador
        await Correios.ConsultarAsync(
            _navegador,
            textoReconhecido,
            transaction,
            ObterConfiguracao().TimeoutEventosSegundos
        );

        _consultasConcluidas++;

        Console.WriteLine(
            $"[PROCESS] Etapa de consulta encerrada: {transaction}");
    }

    private async Task<bool> ProcessarComTentativasAsync(
        string transaction)
    {
        int maxTentativas =
            ObterConfiguracao().MaxTentativasTransacao;

        try
        {
            transaction = ValidadorCodigoRastreamento
                .NormalizarEValidar(transaction);
        }
        catch (FalhaPermanenteTransacaoException ex)
        {
            Console.WriteLine($"[VALIDAÇÃO] {ex.Message}");
            ControleTransacoes.MarcarComoErro(
                CaminhoArquivoAtivo,
                transaction
            );
            return false;
        }

        for (int tentativa = 1;
             tentativa <= maxTentativas;
             tentativa++)
        {
            Console.WriteLine(
                $"[RETRY] Tentativa {tentativa}/{maxTentativas} " +
                $"para {transaction}.");

            try
            {
                if (tentativa > 1)
                    await RecarregarPaginaInicialAsync();

                await ProcessTransaction(transaction);

                ControleTransacoes.MarcarComoOk(
                    CaminhoArquivoAtivo,
                    transaction
                );

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[RETRY] Falha na tentativa {tentativa}: " +
                    $"{ex.GetType().Name}: {ex.Message}");

                if (ex is FalhaPermanenteTransacaoException)
                {
                    ControleTransacoes.MarcarComoErro(
                        CaminhoArquivoAtivo,
                        transaction
                    );

                    Console.WriteLine(
                        $"[RETRY] Falha permanente para {transaction}; " +
                        "novas tentativas não serão executadas.");

                    return false;
                }

                if (tentativa == maxTentativas)
                {
                    ControleTransacoes.MarcarComoErro(
                        CaminhoArquivoAtivo,
                        transaction
                    );

                    Console.WriteLine(
                        $"[RETRY] Limite atingido para {transaction}.");

                    return false;
                }
            }
        }

        return false;
    }

    private async Task RecarregarPaginaInicialAsync()
    {
        Console.WriteLine(
            "[RETRY] Recarregando a página dos Correios...");

        var resultado = await _navegador.LoadUrlAsync(
            ObterConfiguracao().CorreiosUrl
        );

        if (resultado.ErrorCode != CefErrorCode.None ||
            resultado.HttpStatusCode >= 400)
        {
            throw new Exception(
                $"Falha ao recarregar a página: {resultado.ErrorCode}; " +
                $"HTTP {resultado.HttpStatusCode}.");
        }

        Console.WriteLine(
            "[RETRY] Página recarregada; reiniciando a transação.");
    }

    private Configuracao ObterConfiguracao()
    {
        return _configuracao ??
            throw new InvalidOperationException(
                "Configuração ainda não foi carregada.");
    }

    // ETAPA 4 - Encerramento
    public async Task TerminateRobot()
    {
        Console.WriteLine("[TERMINATE] Encerrando automação...");

        CorreiosCaptchaResolver.Encerrar();

        double taxaConclusao = _tentativasOcr == 0
            ? 0
            : (double)_consultasConcluidas / _tentativasOcr * 100;

        Console.WriteLine(
            "[MÉTRICAS] " +
            $"Tentativas OCR: {_tentativasOcr}; " +
            $"consultas concluídas: {_consultasConcluidas}; " +
            $"conclusão por tentativa: {taxaConclusao:F1}%.");

        await Task.CompletedTask;
    }

    // ORQUESTRADOR CENTRAL
    public async Task Execute()
    {
        try
        {
            _configuracao = Configuracao.Carregar(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "config",
                    ".env"
                )
            );

            Console.WriteLine(
                $"[CONFIG] URL carregada; " +
                $"{_configuracao.MaxTentativasTransacao} tentativas; " +
                $"timeout de eventos: " +
                $"{_configuracao.TimeoutEventosSegundos}s.");

            await StartupRoutine();

            while (true)
            {
                var transaction = await GetTransaction();

                if (transaction == null)
                {
                    Console.WriteLine(
                        "[ROBOT] Todas as transações foram percorridas.");
                    break;
                }

                bool concluida =
                    await ProcessarComTentativasAsync(transaction);

                if (concluida)
                {
                    Console.WriteLine(
                        $"[INFO] Etapa finalizada: {transaction}");
                }
                else
                {
                    Console.WriteLine(
                        $"[ERRO TRANSAÇÃO] {transaction}: " +
                        "todas as tentativas falharam.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[ERRO CRÍTICO] {ex}");

            throw;
        }
        finally
        {
            await TerminateRobot();
        }
    }
}
