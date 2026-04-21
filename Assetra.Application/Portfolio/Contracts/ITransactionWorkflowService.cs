using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.Core.Models;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface ITransactionWorkflowService
{
    TransactionWorkflowPlan CreateIncomePlan(IncomeTransactionRequest request);
    TransactionWorkflowPlan CreateCashFlowPlan(CashFlowTransactionRequest request);
    TransactionWorkflowPlan CreateLoanPlan(LoanTransactionRequest request);
    TransactionWorkflowPlan CreateTransferPlan(TransferTransactionRequest request);
}
