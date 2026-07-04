using System;

namespace ModelPulse.UI
{
    public static class WindowPositionHelper
    {
        public static (double Left, double Top) ClampPosition(
            double left, double top, double width, double height,
            double screenLeft, double screenTop, double screenWidth, double screenHeight)
        {
            double minX = screenLeft;
            double maxX = screenLeft + screenWidth - width;
            double minY = screenTop;
            double maxY = screenTop + screenHeight - height;

            double clampedLeft = Math.Max(minX, Math.Min(left, maxX));
            double clampedTop = Math.Max(minY, Math.Min(top, maxY));

            // If the window is larger than the screen bounds, prioritize top-left alignment
            if (maxX < minX)
            {
                clampedLeft = minX;
            }
            if (maxY < minY)
            {
                clampedTop = minY;
            }

            return (clampedLeft, clampedTop);
        }

        public static bool IsOffScreen(
            double left, double top, double width, double height,
            double screenLeft, double screenTop, double screenWidth, double screenHeight)
        {
            return (left + width < screenLeft) ||
                   (left > screenLeft + screenWidth) ||
                   (top + height < screenTop) ||
                   (top > screenTop + screenHeight);
        }
    }
}
