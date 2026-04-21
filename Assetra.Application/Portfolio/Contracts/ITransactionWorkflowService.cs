using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface ITransactionWorkflowService
{
    Task RecordCashDividendAsync(CashDividendTransactionRequest request, CancellationToken ct = default);
    Task RecordStockDividendAsync(StockDividendTransactionRequest request, CancellationToken ct = default);
    Task RecordIncomeAsync(IncomeTransactionRequest request, CancellationToken ct = default);
    Task RecordCashFlowAsync(CashFlowTransactionRequest request, CancellationToken ct = default);
    Task RecordTransferAsync(TransferTransactionRequest request, CancellationToken ct = default);
}
