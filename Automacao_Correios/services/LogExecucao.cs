using System;
using System.IO;
using System.Text;

namespace AutomacaoRPA.Services;

public sealed class LogExecucao : IDisposable
{
    private readonly TextWriter _saidaOriginal;
    private readonly TextWriter _erroOriginal;
    private readonly StreamWriter _arquivo;
    private bool _encerrado;

    public string Caminho { get; }

    private LogExecucao(
        string caminho,
        TextWriter saidaOriginal,
        TextWriter erroOriginal,
        StreamWriter arquivo)
    {
        Caminho = caminho;
        _saidaOriginal = saidaOriginal;
        _erroOriginal = erroOriginal;
        _arquivo = arquivo;
    }

    public static LogExecucao Inicializar(
        string? diretorioBase = null)
    {
        DateTime inicio = DateTime.Now;

        string diretorioLogs = Path.Combine(
            diretorioBase ??
                DiretoriosAplicacao.ObterDiretorioPersistente(),
            "logs"
        );

        Directory.CreateDirectory(diretorioLogs);

        string identificador = inicio.ToString(
            "dd-MM-yyyy_HH-mm"
        );

        string caminho = ObterCaminhoDisponivel(
            diretorioLogs,
            identificador
        );

        var arquivo = new StreamWriter(
            new FileStream(
                caminho,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read
            ),
            new UTF8Encoding(false)
        )
        {
            AutoFlush = true
        };

        TextWriter saidaOriginal = Console.Out;
        TextWriter erroOriginal = Console.Error;
        var bloqueio = new object();

        Console.SetOut(new EscritorMultiplo(
            saidaOriginal,
            arquivo,
            bloqueio
        ));

        Console.SetError(new EscritorMultiplo(
            erroOriginal,
            arquivo,
            bloqueio
        ));

        var log = new LogExecucao(
            caminho,
            saidaOriginal,
            erroOriginal,
            arquivo
        );

        Console.WriteLine(
            $"[LOG] Execução iniciada em " +
            $"{inicio:dd/MM/yyyy HH:mm:ss}.");
        Console.WriteLine($"[LOG] Arquivo: {caminho}");

        return log;
    }

    public void Dispose()
    {
        if (_encerrado)
            return;

        _encerrado = true;

        Console.WriteLine(
            $"[LOG] Execução encerrada em " +
            $"{DateTime.Now:dd/MM/yyyy HH:mm:ss}.");

        Console.Out.Flush();
        Console.Error.Flush();

        Console.SetOut(_saidaOriginal);
        Console.SetError(_erroOriginal);
        _arquivo.Dispose();
    }

    private static string ObterCaminhoDisponivel(
        string diretorio,
        string identificador)
    {
        string caminho = Path.Combine(
            diretorio,
            $"execucao_{identificador}.log"
        );

        int numero = 2;

        while (File.Exists(caminho))
        {
            caminho = Path.Combine(
                diretorio,
                $"execucao_{identificador}_{numero:00}.log"
            );

            numero++;
        }

        return caminho;
    }

    private sealed class EscritorMultiplo : TextWriter
    {
        private readonly TextWriter _principal;
        private readonly TextWriter _arquivo;
        private readonly object _bloqueio;

        public EscritorMultiplo(
            TextWriter principal,
            TextWriter arquivo,
            object bloqueio)
        {
            _principal = principal;
            _arquivo = arquivo;
            _bloqueio = bloqueio;
        }

        public override Encoding Encoding =>
            _principal.Encoding;

        public override void Write(char value)
        {
            lock (_bloqueio)
            {
                _principal.Write(value);
                _arquivo.Write(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_bloqueio)
            {
                _principal.Write(value);
                _arquivo.Write(value);
            }
        }

        public override void WriteLine(string? value)
        {
            lock (_bloqueio)
            {
                _principal.WriteLine(value);
                _arquivo.WriteLine(value);
            }
        }

        public override void Flush()
        {
            lock (_bloqueio)
            {
                _principal.Flush();
                _arquivo.Flush();
            }
        }
    }
}
