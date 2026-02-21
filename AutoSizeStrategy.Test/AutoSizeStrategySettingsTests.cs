using System;
using System.Collections.Generic;
using AutoSizeStrategy;
using Moq;
using TradingPlatform.BusinessLayer;
using Xunit;

namespace AutoSizeStrategy.Test
{
    public class AutoSizeStrategySettingsTests
    {
        [Theory]
        [InlineData("0.25, 0.25, 1", new[] { 0.25, 0.25, 1.0 })] // standard
        [InlineData("0.25,1", new[] { 0.25, 1.0 })] // 2 shots
        [InlineData("0.2,0.3,0.5,1", new[] { 0.2, 0.3, 0.5, 1.0 })] // 4 shots
        public void TryParseClutchSequence_ValidInput_ReturnsParsedArray(
            string input,
            double[] expected
        )
        {
            Assert.True(AutoSizeStrategy.TryParseClutchSequence(input, out double[] parsed));
            Assert.Equal(expected, parsed);
        }

        [Theory]
        [InlineData("1,2,a")] // not numbers
        [InlineData("0.25, 1.5")] // value > 1
        [InlineData("0.25, -1, 1")] // negative value
        [InlineData("")] // empty
        public void TryParseClutchSequence_InvalidInput_ReturnsNull(string input) =>
            Assert.False(AutoSizeStrategy.TryParseClutchSequence(input, out _));
    }
}
