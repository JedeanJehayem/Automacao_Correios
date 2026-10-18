using System;

namespace AutomacaoRPA.Services;

public sealed class FalhaPermanenteTransacaoException : Exception
{
    public FalhaPermanenteTransacaoException(string mensagem)
        : base(mensagem)
    {
    }
}
