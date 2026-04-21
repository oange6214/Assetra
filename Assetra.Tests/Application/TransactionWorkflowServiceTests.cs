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

    [Fact]
    public async Task RecordIncomeAsync_WithFee_RecordsMainTradeAndFeeTrade()
    {
        var txService = new Mock<ITransactionService>();
        var sut = new TransactionWorkflowService(txService.Object);
        var request = new IncomeTransactionRequest(
            Amount: 5000m,
            TradeDate: new DateTime(2026, 1, 1),
            CashAccountId: null,
            Note: "薪資",
            Fee: 50m);

        await sut.RecordIncomeAsync(request);

        txService.Verify(s => s.RecordAsync(
            It.Is<Trade>(t => t.Type == TradeType.Income && t.CashAmount == 5000m)),
            Times.Once);
        txService.Verify(s => s.RecordAsync(
            It.Is<Trade>(t => t.Type == TradeType.Withdrawal && t.CashAmount == 50m)),
            Times.Once);
        txService.Verify(s => s.RecordAsync(It.IsAny<Trade>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RecordCashDividendAsync_RecordsCashDividendTrade()
    {
        var txService = new Mock<ITransactionService>();
        var sut = new TransactionWorkflowService(txService.Object);
        var request = new CashDividendTransactionRequest(
            Symbol: "2330",
            Exchange: "TWSE",
            Name: "台積電",
            PerShare: 3m,
            Quantity: 1000,
            TotalAmount: 3000m,
            TradeDate: new DateTime(2026, 3, 1),
            CashAccountId: null,
            Fee: 0m);

        await sut.RecordCashDividendAsync(request);

        txService.Verify(s => s.RecordAsync(
            It.Is<Trade>(t => t.Type == TradeType.CashDividend && t.CashAmount == 3000m)),
            Times.Once);
    }

    [Fact]
    public async Task RecordCashDividendAsync_WithFee_RecordsMainTradeAndFeeTrade()
    {
        var txService = new Mock<ITransactionService>();
        var sut = new TransactionWorkflowService(txService.Object);
        var request = new CashDividendTransactionRequest(
            Symbol: "2330",
            Exchange: "TWSE",
            Name: "台積電",
            PerShare: 3m,
            Quantity: 1000,
            TotalAmount: 3000m,
            TradeDate: new DateTime(2026, 3, 1),
            CashAccountId: null,
            Fee: 20m);

        await sut.RecordCashDividendAsync(request);

        txService.Verify(s => s.RecordAsync(
            It.Is<Trade>(t => t.Type == TradeType.CashDividend && t.CashAmount == 3000m)),
            Times.Once);
        txService.Verify(s => s.RecordAsync(
            It.Is<Trade>(t => t.Type == TradeType.Withdrawal && t.CashAmount == 20m)),
            Times.Once);
        txService.Verify(s => s.RecordAsync(It.IsAny<Trade>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RecordTransferAsync_SameAmount_RecordsSingleTransferTrade()
    {
        var txService = new Mock<ITransactionService>();
        var sut = new TransactionWorkflowService(txService.Object);
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var request = new TransferTransactionRequest(
            SourceCashAccountId: sourceId,
            SourceName: "帳戶A",
            DestinationCashAccountId: destinationId,
            DestinationName: "帳戶B",
            SourceAmount: 10000m,
            DestinationAmount: 10000m,
            TradeDate: new DateTime(2026, 2, 1),
            Note: null,
            Fee: 0m);

        await sut.RecordTransferAsync(request);

        txService.Verify(s => s.RecordAsync(
            It.Is<Trade>(t => t.Type == TradeType.Transfer && t.CashAmount == 10000m)),
            Times.Once);
        txService.Verify(s => s.RecordAsync(It.IsAny<Trade>()), Times.Once);
    }

    [Fact]
    public async Task RecordTransferAsync_SameAmount_WithFee_RecordsTransferAndFeeTrade()
    {
        var txService = new Mock<ITransactionService>();
        var sut = new TransactionWorkflowService(txService.Object);
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var request = new TransferTransactionRequest(
            SourceCashAccountId: sourceId,
            SourceName: "帳戶A",
            DestinationCashAccountId: destinationId,
            DestinationName: "帳戶B",
            SourceAmount: 10000m,
            DestinationAmount: 10000m,
            TradeDate: new DateTime(2026, 2, 1),
            Note: null,
            Fee: 30m);

        await sut.RecordTransferAsync(request);

        txService.Verify(s => s.RecordAsync(
            It.Is<Trade>(t => t.Type == TradeType.Transfer && t.CashAmount == 10000m)),
            Times.Once);
        txService.Verify(s => s.RecordAsync(
            It.Is<Trade>(t => t.Type == TradeType.Withdrawal && t.CashAmount == 30m)),
            Times.Once);
        txService.Verify(s => s.RecordAsync(It.IsAny<Trade>()), Times.Exactly(2));
    }
}
