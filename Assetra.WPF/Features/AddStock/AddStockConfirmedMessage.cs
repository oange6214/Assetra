using Assetra.Core.Models;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Assetra.WPF.Features.AddStock;

public class AddStockConfirmedMessage : ValueChangedMessage<StockSearchResult>
{
    public AddStockConfirmedMessage(StockSearchResult result) : base(result) { }
}
