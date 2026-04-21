using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Application;

public sealed class TransactionWorkflowServiceTests
{
    [Fact]
    public async Task RecordIncomeAsync_RecordsTradeThroughTransactionService()
    {
        var txService = new Mock<ITransactionService>();
        var sut = new TransactionWorkflowService(txService.Object);
        var request = new IncomeTransactionRequest(
            Amount: 5000m,
            TradeDate: new DateTime(2026, 1, 1),
            CashAccountId: null,
            Note: "薪資",
            Fee: 0m);

        await sut.RecordIncomeAsync(request);

        txService.Verify(s => s.RecordAsync(
            It.Is<Trade>(t => t.Type == TradeType.Income && t.CashAmount == 5000m)),
            Times.Once);
    }
}
