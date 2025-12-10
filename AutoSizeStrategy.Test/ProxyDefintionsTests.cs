using System.Threading;
using AutoSizeStrategy;
using TradingPlatform.BusinessLayer;
using Xunit;

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
    }
}
