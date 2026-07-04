using System;
using Xunit;
using FluentAssertions;
using ModelPulse.Core.Collections;

namespace ModelPulse.Tests
{
    public class CircularBufferTests
    {
        [Fact]
        public void CircularBuffer_EnforcesCapacityLimit_AndRollsOver()
        {
            // Arrange
            var buffer = new CircularBuffer<int>(3);

            // Act
            buffer.Enqueue(1);
            buffer.Enqueue(2);
            buffer.Enqueue(3);

            // Assert
            buffer.Count.Should().Be(3);
            buffer.Should().ContainInOrder(1, 2, 3);

            // Act: Enqueue 4th item (should overwrite 1)
            buffer.Enqueue(4);

            // Assert
            buffer.Count.Should().Be(3);
            buffer.Should().ContainInOrder(2, 3, 4);

            // Act: Enqueue 5th item (should overwrite 2)
            buffer.Enqueue(5);

            // Assert
            buffer.Count.Should().Be(3);
            buffer.Should().ContainInOrder(3, 4, 5);
        }

        [Fact]
        public void CircularBuffer_Dequeue_RemovesFirstElement()
        {
            // Arrange
            var buffer = new CircularBuffer<string>(3);
            buffer.Enqueue("A");
            buffer.Enqueue("B");

            // Act & Assert
            buffer.Dequeue().Should().Be("A");
            buffer.Count.Should().Be(1);
            buffer.Peek().Should().Be("B");

            buffer.Dequeue().Should().Be("B");
            buffer.Count.Should().Be(0);

            // Dequeueing empty should throw
            Assert.Throws<InvalidOperationException>(() => buffer.Dequeue());
        }

        [Fact]
        public void CircularBuffer_Clear_EmptyBuffer()
        {
            // Arrange
            var buffer = new CircularBuffer<double>(2);
            buffer.Enqueue(1.1);
            buffer.Enqueue(2.2);

            // Act
            buffer.Clear();

            // Assert
            buffer.Count.Should().Be(0);
            buffer.Should().BeEmpty();
        }
    }
}
