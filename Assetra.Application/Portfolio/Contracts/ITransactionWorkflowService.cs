using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.Core.Models;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface ITransactionWorkflowService
{
    TransactionWorkflowPlan CreateCashDividendPlan(CashDividendTransactionRequest request);
    TransactionWorkflowPlan CreateStockDividendPlan(StockDividendTransactionRequest request);
    TransactionWorkflowPlan CreateIncomePlan(IncomeTransactionRequest request);
    TransactionWorkflowPlan CreateCashFlowPlan(CashFlowTransactionRequest request);
    TransactionWorkflowPlan CreateLoanPlan(LoanTransactionRequest request);
    TransactionWorkflowPlan CreateTransferPlan(TransferTransactionRequest request);
}
