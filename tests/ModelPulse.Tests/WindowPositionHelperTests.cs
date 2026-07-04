using FluentAssertions;
using ModelPulse.UI;
using Xunit;

namespace ModelPulse.Tests
{
    public class WindowPositionHelperTests
    {
        private const double ScreenLeft = 0;
        private const double ScreenTop = 0;
        private const double ScreenWidth = 1920;
        private const double ScreenHeight = 1080;
        private const double WinWidth = 320;
        private const double WinHeight = 420;

        [Fact]
        public void ClampPosition_WithinBounds_ReturnsSamePosition()
        {
            // Arrange
            double x = 100;
            double y = 200;

            // Act
            var (clampedX, clampedY) = WindowPositionHelper.ClampPosition(
                x, y, WinWidth, WinHeight, ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

            // Assert
            clampedX.Should().Be(x);
            clampedY.Should().Be(y);
        }

        [Fact]
        public void ClampPosition_LeftExceeded_ClampsToLeftBound()
        {
            // Arrange
            double x = -50;
            double y = 200;

            // Act
            var (clampedX, clampedY) = WindowPositionHelper.ClampPosition(
                x, y, WinWidth, WinHeight, ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

            // Assert
            clampedX.Should().Be(ScreenLeft);
            clampedY.Should().Be(y);
        }

        [Fact]
        public void ClampPosition_RightExceeded_ClampsToRightBound()
        {
            // Arrange
            double x = 1800; // 1800 + 320 = 2120 > 1920
            double y = 200;

            // Act
            var (clampedX, clampedY) = WindowPositionHelper.ClampPosition(
                x, y, WinWidth, WinHeight, ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

            // Assert
            clampedX.Should().Be(ScreenWidth - WinWidth);
            clampedY.Should().Be(y);
        }

        [Fact]
        public void ClampPosition_TopExceeded_ClampsToTopBound()
        {
            // Arrange
            double x = 100;
            double y = -20;

            // Act
            var (clampedX, clampedY) = WindowPositionHelper.ClampPosition(
                x, y, WinWidth, WinHeight, ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

            // Assert
            clampedX.Should().Be(x);
            clampedY.Should().Be(ScreenTop);
        }

        [Fact]
        public void ClampPosition_BottomExceeded_ClampsToBottomBound()
        {
            // Arrange
            double x = 100;
            double y = 800; // 800 + 420 = 1220 > 1080

            // Act
            var (clampedX, clampedY) = WindowPositionHelper.ClampPosition(
                x, y, WinWidth, WinHeight, ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

            // Assert
            clampedX.Should().Be(x);
            clampedY.Should().Be(ScreenHeight - WinHeight);
        }

        [Fact]
        public void IsOffScreen_CompletelyWithinBounds_ReturnsFalse()
        {
            // Arrange
            double x = 100;
            double y = 200;

            // Act
            bool result = WindowPositionHelper.IsOffScreen(
                x, y, WinWidth, WinHeight, ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsOffScreen_PartiallyOnScreen_ReturnsFalse()
        {
            // Arrange
            double x = -100; // Left is off-screen, but right is on-screen (-100 + 320 = 220)
            double y = 200;

            // Act
            bool result = WindowPositionHelper.IsOffScreen(
                x, y, WinWidth, WinHeight, ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsOffScreen_CompletelyOffScreenLeft_ReturnsTrue()
        {
            // Arrange
            double x = -350; // -350 + 320 = -30 < 0
            double y = 200;

            // Act
            bool result = WindowPositionHelper.IsOffScreen(
                x, y, WinWidth, WinHeight, ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void IsOffScreen_CompletelyOffScreenRight_ReturnsTrue()
        {
            // Arrange
            double x = 1950;
            double y = 200;

            // Act
            bool result = WindowPositionHelper.IsOffScreen(
                x, y, WinWidth, WinHeight, ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void IsOffScreen_CompletelyOffScreenTop_ReturnsTrue()
        {
            // Arrange
            double x = 100;
            double y = -450;

            // Act
            bool result = WindowPositionHelper.IsOffScreen(
                x, y, WinWidth, WinHeight, ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void IsOffScreen_CompletelyOffScreenBottom_ReturnsTrue()
        {
            // Arrange
            double x = 100;
            double y = 1100;

            // Act
            bool result = WindowPositionHelper.IsOffScreen(
                x, y, WinWidth, WinHeight, ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

            // Assert
            result.Should().BeTrue();
        }
    }
}
