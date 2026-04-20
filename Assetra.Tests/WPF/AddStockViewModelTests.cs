using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Moq;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.AddStock;
using Xunit;

namespace Assetra.Tests.WPF;

public class AddStockViewModelTests
{
    [Fact]
    public void Search_EmptyQuery_ReturnsNoResults()
    {
        var mockSearch = new Mock<IStockSearchService>();
        var vm = new AddStockViewModel(mockSearch.Object, ImmediateScheduler.Instance);
        vm.SearchQuery = "";

        Assert.Empty(vm.SearchResults);
        mockSearch.Verify(s => s.Search(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Search_NonEmptyQuery_CallsSearchService()
    {
        var mockSearch = new Mock<IStockSearchService>();
        mockSearch.Setup(s => s.Search("2330")).Returns([
            new StockSearchResult("2330", "台積電", "TWSE")
        ]);

        var vm = new AddStockViewModel(mockSearch.Object, ImmediateScheduler.Instance);
        vm.SearchQuery = "2330";

        // ImmediateScheduler makes Throttle fire instantly
        Assert.Single(vm.SearchResults);
        Assert.Equal("台積電", vm.SearchResults[0].Name);
    }

    [Fact]
    public void SelectedResult_EnablesAddCommand()
    {
        var mockSearch = new Mock<IStockSearchService>();
        mockSearch.Setup(s => s.Search(It.IsAny<string>())).Returns([
            new StockSearchResult("2330", "台積電", "TWSE")
        ]);

        var vm = new AddStockViewModel(mockSearch.Object, ImmediateScheduler.Instance);
        vm.SearchQuery = "2330";
        vm.SelectedResult = vm.SearchResults[0];

        Assert.True(vm.AddCommand.CanExecute(null));
    }
}
