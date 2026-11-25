namespace Vairas.Scheduling.Business;

/// <summary>The command was accepted but its outcome is still being reconciled; the caller retries with the same operation identity.</summary>
public sealed class OperationPendingException(string code, string message = BusinessException.DefaultMessage, Exception? innerException = null)
    : BusinessException(code, message, innerException);
