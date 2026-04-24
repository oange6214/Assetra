using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

public sealed class CreditCardTransactionWorkflowService : ICreditCardTransactionWorkflowService
{
    private readonly IAssetRepository _assetRepository;
    private readonly ITransactionService _transactionService;

    public CreditCardTransactionWorkflowService(
        IAssetRepository assetRepository,
        ITransactionService transactionService)
    {
        _assetRepository = assetRepository;
        _transactionService = transactionService;
    }

    public async Task<CreditCardTransactionResult> ChargeAsync(
        CreditCardChargeRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureCreditCardAsync(request.CreditCardAssetId).ConfigureAwait(false);

        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: request.CardName,
            Exchange: string.Empty,
            Name: request.CardName,
            Type: TradeType.CreditCardCharge,
            TradeDate: request.TradeDate,
            Price: 0m,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: request.Amount,
            Note: NormalizeNote(request.Note),
            LiabilityAssetId: request.CreditCardAssetId);

        await _transactionService.RecordAsync(trade).ConfigureAwait(false);
        return new CreditCardTransactionResult(trade);
    }

    public async Task<CreditCardTransactionResult> PayAsync(
        CreditCardPaymentRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureCreditCardAsync(request.CreditCardAssetId).ConfigureAwait(false);

        var note = string.IsNullOrWhiteSpace(request.Note)
            ? $"繳款自 {request.CashAccountName}"
            : request.Note.Trim();

        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: request.CardName,
            Exchange: string.Empty,
            Name: request.CardName,
            Type: TradeType.CreditCardPayment,
            TradeDate: request.TradeDate,
            Price: 0m,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: request.Amount,
            CashAccountId: request.CashAccountId,
            Note: note,
            LiabilityAssetId: request.CreditCardAssetId);

        await _transactionService.RecordAsync(trade).ConfigureAwait(false);
        return new CreditCardTransactionResult(trade);
    }

    private async Task EnsureCreditCardAsync(Guid assetId)
    {
        var items = await _assetRepository.GetItemsAsync().ConfigureAwait(false);
        var card = items.FirstOrDefault(i => i.Id == assetId);
        if (card is null)
            throw new InvalidOperationException("找不到指定的信用卡負債資產。");
        if (card.Type != FinancialType.Liability || card.LiabilitySubtype != LiabilitySubtype.CreditCard)
            throw new InvalidOperationException("指定資產不是信用卡負債。");
    }

    private static string? NormalizeNote(string? note)
        => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
