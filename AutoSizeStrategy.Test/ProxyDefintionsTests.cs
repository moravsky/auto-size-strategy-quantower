using System.Runtime.CompilerServices;
using Moq;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy.Test
{
    public class ProxyDefinitionsTests
    {
        [Fact]
        public void Create_WithPlaceOrderParams_ReturnsSpecificWrapper()
        {
            var sdkParams = new PlaceOrderRequestParameters();
            var result = RequestParametersWrapper.Create(sdkParams);

            Assert.IsType<PlaceOrderRequestParametersWrapper>(result);
            var specific = result as IPlaceOrderRequestParameters;
            Assert.NotNull(specific);
        }

        [Fact]
        public void Create_WithUnknownParams_ReturnsGenericWrapper()
        {
            var sdkParams = new HistoryRequestParameters();
            var result = RequestParametersWrapper.Create(sdkParams);

            Assert.IsType<RequestParametersWrapper<RequestParameters>>(result);
            Assert.IsNotType<PlaceOrderRequestParametersWrapper>(result);
        }

        [Fact]
        public void Create_WithModifyOrderParams_ReturnsSpecificWrapper()
        {
            var sdkParams = new ModifyOrderRequestParameters();
            var result = RequestParametersWrapper.Create(sdkParams);

            Assert.IsType<ModifyOrderRequestParametersWrapper>(result);
            Assert.NotNull(result as IModifyOrderRequestParameters);
        }

        [Fact]
        public void FromModify_TransfersModifyData_Correctly()
        {
            var accMock = new Mock<IAccount>();
            accMock.SetupGet(a => a.Id).Returns("RealAcc123");

            var symMock = new Mock<ISymbol>();
            symMock.SetupGet(s => s.Id).Returns("MES");

            var modifyMock = new Mock<IModifyOrderRequestParameters>();
            modifyMock.SetupGet(m => m.Account).Returns(accMock.Object);
            modifyMock.SetupGet(m => m.Symbol).Returns(symMock.Object);
            modifyMock.SetupGet(m => m.Side).Returns(Side.Buy);
            modifyMock.SetupGet(m => m.Price).Returns(5000.0);
            modifyMock.SetupGet(m => m.OrderTypeId).Returns(OrderType.Limit);
            modifyMock.SetupGet(m => m.TimeInForce).Returns(TimeInForce.GTC);

            var stopLossItems = new List<SlTpHolder>()
            {
                SlTpHolder.CreateSL(4995),
                SlTpHolder.CreateSL(40, PriceMeasurement.Offset),
            };
            modifyMock.SetupGet(m => m.StopLossItems).Returns(stopLossItems);

            var takeProfitItems = new List<SlTpHolder>()
            {
                SlTpHolder.CreateTP(5005),
                SlTpHolder.CreateTP(40, PriceMeasurement.Offset),
            };
            modifyMock.SetupGet(m => m.TakeProfitItems).Returns(takeProfitItems);

            var result =
                IPlaceOrderRequestParameters.FromModify(modifyMock.Object, 5.0)
                as PlaceOrderRequestParametersWrapper;
            Assert.NotNull(result);

            // Verify the wrapper is fully hydrated
            Assert.Equal(Side.Buy, result.Side);
            Assert.Equal(5000.0, result.Price);
            Assert.Equal(5.0, result.Quantity);
            Assert.Equal(OrderType.Limit, result.OrderTypeId);
            Assert.Same(accMock.Object, result.Account);
            Assert.Same(symMock.Object, result.Symbol);
            Assert.Equal(stopLossItems, result.StopLossItems);
            Assert.Equal(takeProfitItems, result.TakeProfitItems);

            Assert.Equal(Side.Buy, result.Inner.Side);
            Assert.Equal(5000.0, result.Inner.Price);
            Assert.Equal(5.0, result.Inner.Quantity);
            Assert.Equal("Limit", result.Inner.OrderTypeId);
            Assert.Equal(TimeInForce.GTC, result.TimeInForce);
            Assert.Equal(TimeInForce.GTC, result.Inner.TimeInForce);
        }

        [Fact]
        public void FromModify_BracketsMatchQuantity_RegardlessOfAssignmentOrder()
        {
            var wrapper = new PlaceOrderRequestParametersWrapper();
            var sl = SlTpHolder.CreateSL(10, PriceMeasurement.Offset);
            sl.Quantity = 1; // Start with wrong quantity

            // Act: Set StopLoss then Quantity
            wrapper.StopLossItems = [sl];
            wrapper.Quantity = 5;

            // Assert
            Assert.Equal(5, wrapper.Inner.StopLossItems[0].Quantity);
        }

        private static Account CreateFakeAccount(double balance)
        {
            var account = (Account)RuntimeHelpers.GetUninitializedObject(typeof(Account));

            typeof(Account).GetProperty("Balance")!.SetValue(account, balance);

            return account;
        }

        [Fact]
        public void Balance_DelegatesToSdkAccount_NotCachedAtConstruction()
        {
            var account = CreateFakeAccount(150_000);
            var wrapper = new AccountWrapper(account);

            Assert.Equal(150_000, wrapper.Balance);

            typeof(Account).GetProperty("Balance")!.SetValue(account, 148_500);

            Assert.Equal(148_500, wrapper.Balance);
        }
    }
}
