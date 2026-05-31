using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

public sealed class AccountUpsertWorkflowService : IAccountUpsertWorkflowService
{
    // 系統 group GUID（與 AssetSchemaMigrator 的常數一致）。
    // 不直接 reference Infrastructure.AssetSchemaMigrator 是為了不讓 Application 層相依 Infrastructure。
    private static readonly Guid GrpBankAccount = new("11111111-1111-1111-1111-111111111101");
    private static readonly Guid GrpCashOnHand = new("11111111-1111-1111-1111-111111111104");
    private static readonly Guid GrpBrokerageSettlement = new("11111111-1111-1111-1111-111111111105");
    private static readonly Guid GrpEPayment = new("11111111-1111-1111-1111-111111111106");

    private readonly IAssetRepository _assetRepository;

    public AccountUpsertWorkflowService(IAssetRepository assetRepository)
    {
        _assetRepository = assetRepository;
    }

    public async Task<AccountUpsertResult> CreateAsync(CreateAccountRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var subtype = string.IsNullOrWhiteSpace(request.Subtype) ? null : request.Subtype.Trim();
        var name = request.Name.Trim();
        var account = new AssetItem(
            Guid.NewGuid(),
            name,
            FinancialType.Asset,
            ResolveGroupIdForSubtype(subtype),
            request.Currency,
            request.CreatedDate,
            Subtype: subtype);

        // 用 CreateOrRevive 而非裸 AddItem：同名同幣別的「已刪除/已封存」帳戶會被就地復活，
        // 避免撞上 (name, currency) 唯一索引（該索引把墓碑列也算進去）而丟出未處理的 SQLite 例外。
        var outcome = await _assetRepository.CreateOrReviveAccountAsync(account, ct).ConfigureAwait(false);
        if (outcome.Status == AccountCreateStatus.DuplicateActive)
            throw new DuplicateAccountException(name, request.Currency);

        var persisted = await _assetRepository.GetByIdAsync(outcome.Id).ConfigureAwait(false);
        return new AccountUpsertResult(persisted ?? account with { Id = outcome.Id });
    }

    public async Task<AccountUpsertResult> UpdateAsync(UpdateAccountRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var existing = await _assetRepository.GetByIdAsync(request.AccountId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Cash account '{request.AccountId}' was not found.");
        if (existing.Type != FinancialType.Asset)
            throw new InvalidOperationException($"Asset '{request.AccountId}' is not a cash account.");

        var subtype = string.IsNullOrWhiteSpace(request.Subtype) ? null : request.Subtype.Trim();
        var account = existing with
        {
            Name = request.Name.Trim(),
            GroupId = ResolveGroupIdForSubtype(subtype),
            Currency = request.Currency,
            CreatedDate = request.CreatedDate,
            Subtype = subtype,
        };

        await _assetRepository.UpdateItemAsync(account).ConfigureAwait(false);
        var persisted = await _assetRepository.GetByIdAsync(account.Id).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Cash account '{request.AccountId}' disappeared after update.");
        return new AccountUpsertResult(persisted);
    }

    public Task<Guid> FindOrCreateAccountAsync(string name, string currency, CancellationToken ct = default) =>
        _assetRepository.FindOrCreateAccountAsync(name, currency, ct);

    /// <summary>
    /// 把 dialog subtype 對應到財務總覽分組。新加的 subtype 要加在這裡。
    /// 未對應的（自訂、未填）→ null，落入「其他」分組。
    /// </summary>
    private static Guid? ResolveGroupIdForSubtype(string? subtype) => subtype switch
    {
        "現金" or "手邊現金" => GrpCashOnHand,
        "證券交割戶" => GrpBrokerageSettlement,
        "電子支付" or "儲值卡" => GrpEPayment,
        "銀行活存" or "數位活存" or "定期存款" or "外幣活存" => GrpBankAccount,
        _ => null,
    };
}
