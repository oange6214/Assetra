using CommunityToolkit.Mvvm.Messaging.Messages;
using Assetra.Core.Models;

namespace Assetra.WPF.Features.AddStock;

public class AddStockConfirmedMessage : ValueChangedMessage<StockSearchResult>
{
    public AddStockConfirmedMessage(StockSearchResult result) : base(result) { }
}
