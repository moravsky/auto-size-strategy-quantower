using Moq;
using TradingPlatform.BusinessLayer;
using static AutoSizeStrategy.Test.SdkTestHelpers;

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
            var account = CreateFakeAccount(balance: 150_000);

            var sdkModify = new ModifyOrderRequestParameters
            {
                Account = account,
                Side = Side.Buy,
                Price = 5000.0,
                OrderTypeId = OrderType.Limit,
                TimeInForce = TimeInForce.GTC,
                Comment = "test-comment",
                Slippage = 3,
            };
            sdkModify.StopLossItems.Add(SlTpHolder.CreateSL(4995));
            sdkModify.StopLossItems.Add(SlTpHolder.CreateSL(40, PriceMeasurement.Offset));
            sdkModify.TakeProfitItems.Add(SlTpHolder.CreateTP(5005));
            sdkModify.TakeProfitItems.Add(SlTpHolder.CreateTP(40, PriceMeasurement.Offset));

            var modify = new ModifyOrderRequestParametersWrapper(sdkModify);

            var result =
                IPlaceOrderRequestParameters.FromModify(modify, 5.0)
                as PlaceOrderRequestParametersWrapper;
            Assert.NotNull(result);

            // Verify the wrapper is fully hydrated
            Assert.Equal(Side.Buy, result.Side);
            Assert.Equal(5000.0, result.Price);
            Assert.Equal(5.0, result.Quantity);
            Assert.Equal(OrderType.Limit, result.OrderTypeId);
            Assert.Equal(TimeInForce.GTC, result.TimeInForce);
            Assert.Equal("test-comment", result.Inner.Comment);
            Assert.Equal(3, result.Inner.Slippage);
            Assert.Equal(2, result.Inner.StopLossItems.Count);
            Assert.Equal(2, result.Inner.TakeProfitItems.Count);
            Assert.Equal(5.0, result.Inner.StopLossItems[0].Quantity);
            Assert.Equal(5.0, result.Inner.TakeProfitItems[0].Quantity);
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

        [Fact]
        public void Balance_DelegatesToSdkAccount_NotCachedAtConstruction()
        {
            var account = CreateFakeAccount(balance: 150_000);
            var wrapper = new AccountWrapper(account);

            Assert.Equal(150_000, wrapper.Balance);

            typeof(Account).GetProperty("Balance")!.SetValue(account, 148_500);

            Assert.Equal(148_500, wrapper.Balance);
        }
    }
}
